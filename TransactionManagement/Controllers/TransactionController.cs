using System.Data.SqlClient;
using System.Globalization;
using CsvHelper;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.SqlServer.Storage.Internal;
using Newtonsoft.Json.Linq;
using NodaTime;
using NodaTime.Extensions;
using Npgsql;
using OfficeOpenXml;
using TimeZoneConverter;
using TransactionManagement.Models;

namespace TransactionManagement.Controllers;

[ApiController]
[Route("[action]")]
public class TransactionController(IConfiguration configuration) : ControllerBase
{
    private readonly SqlConnection connectionDb =
        new SqlConnection(configuration.GetConnectionString("AppDbContext"));

    private const string _apiKey = "5RGNDXDZNVE1";

    


    [HttpGet]
    public async Task<ActionResult> GetTransactionBetweenTwoDataUserOffsetAsync(DateTime firstData, DateTime secondData)
    {
        var timeZone = DateTimeOffset.Now.Offset;
        var firstDataOffset = new DateTimeOffset(firstData, timeZone);
        var secondDataOffset = new DateTimeOffset(secondData, timeZone);
        await using (connectionDb)
        {
            await connectionDb.OpenAsync();
            const string sql = @"
                SELECT * 
                FROM transactions 
                WHERE transactions.transaction_date >= @FirstData 
                AND transactions.transaction_date <= @SecondData";

            var parameters = new { FirstData = firstDataOffset, SecondData = secondDataOffset };
            var data = await connectionDb.QueryAsync<Transaction>(sql, parameters);

            foreach (var item in data)
            {
                item.transaction_date = item.transaction_date.UtcDateTime;
            }
            
                
            return Ok(data);
        }
        
    }
    
    [HttpGet]
    public async Task<ActionResult> GetTransactionBetweenTwoDataClientOffsetAsync(DateTimeOffset firstData, DateTimeOffset secondData)
    {
        await using (connectionDb)
        {
            await connectionDb.OpenAsync();
            const string sql = @"
                SELECT *
                FROM transactions
                WHERE  transaction_date >= @FirstData
                AND transaction_date <= @SecondData";

            var parameters = new { FirstData = firstData, SecondData = secondData };
            var data = await connectionDb.QueryAsync<Transaction>(sql, parameters);

            foreach (var item in data)
            {
                item.transaction_date = item.transaction_date.UtcDateTime;
            }
            
                
            return Ok(data);
        }
        
    }


    [HttpGet]
    public async Task<ActionResult> ExportToExcelAsync()
    {
        await using (connectionDb)
        {
            const string query = "SELECT * FROM transactions";
            var data = (await connectionDb.QueryAsync(query)).ToList();

            if (data.Count == 0)
            {
                return NotFound("No data found to export.");
            }

            var fileStream = new MemoryStream();
            using (var package = new ExcelPackage(fileStream))
            {
                var worksheet = package.Workbook.Worksheets.Add("Data");

                // Добавление заголовков
                var properties = ((IDictionary<string, object>)data.First()).Keys.ToList();
                for (int i = 0; i < properties.Count; i++)
                {
                    worksheet.Cells[1, i + 1].Value = properties[i];
                }

                // Добавление данных
                int row = 2;
                foreach (var item in data)
                {
                    var values = ((IDictionary<string, object>)item).Values.ToList();
                    for (int col = 0; col < values.Count; col++)
                    {
                        worksheet.Cells[row, col + 1].Value = values[col];
                    }

                    row++;
                }

                await package.SaveAsync();
            }

            fileStream.Position = 0;
            var excelName = $"ExportedData-{DateTime.Now:yyyyMMddHHmmssfff}.xlsx";
            return File(fileStream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", excelName);
        }
    }
    
    [HttpPost]
    public async Task<string> DownloadDataAsync(IFormFile file)
    {
        try
        {
            var data = ReadCsvFile(file);
            foreach (var item in data)
            {
                var timeZoneInfo = TZConvert.GetTimeZoneInfo(await GetTimeZoneAsync(item.client_location)).GetUtcOffset(item.transaction_date);
                item.transaction_date = new DateTimeOffset(item.transaction_date.DateTime, timeZoneInfo);
            }
            await using (connectionDb)
            {
                if (connectionDb is null) return default;

                await connectionDb.OpenAsync();


                const string sql = @"
                MERGE INTO Transactions AS target
                USING (VALUES (@transaction_id, @name, @email, @amount, @transaction_date, @client_location)) 
                    AS source (transaction_id, name, email, amount, transaction_date, client_location)
                ON target.transaction_id = source.transaction_id
                WHEN MATCHED THEN
                    UPDATE SET 
                        name = source.name,
                        email = source.email,
                        amount = source.amount,
                        transaction_date = source.transaction_date,
                        client_location = source.client_location
                WHEN NOT MATCHED THEN
                    INSERT (transaction_id, name, email, amount, transaction_date, client_location)
                    VALUES (source.transaction_id, source.name, source.email, source.amount, source.transaction_date, source.client_location);";

                await connectionDb.ExecuteAsync(sql, data);
            }
        }
        catch (ArgumentNullException e)
        {
            Console.WriteLine(e);
            throw;
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
            throw;
        }

        return "File download";
    }


    private static List<Transaction> ReadCsvFile(IFormFile file)
    {
        using var stream = file.OpenReadStream();
        using var reader = new StreamReader(stream);
        using var csv = new CsvReader(reader, CultureInfo.InvariantCulture);
        
        var records = csv.GetRecords<Transaction>().ToList();
        return records;
    }
    

    private static readonly HttpClient client = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(10) // Set an appropriate timeout
    };

    private async Task<string> GetTimeZoneAsync(string location)
    {
        var attempts = 0;
        const int maxRetries = 3; 
        
        var coordinate = "latitude=" + location
            .Replace(" ", "")
            .Replace(",", "&longitude=");
        var url = $"https://timeapi.io/api/Time/current/coordinate?{coordinate}";

        while (attempts < maxRetries)
        {
            try
            {
                var response = await client.GetAsync(url).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    var responseBody = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                    var json = JObject.Parse(responseBody);
                    var timeZone = json["timeZone"].ToString();
                    return timeZone;
                }
            }
            catch (Exception ex)
            {
                if (attempts >= maxRetries)
                {
                    Console.WriteLine("Max retries reached. Exiting.");
                    throw;
                }
                Console.WriteLine(ex.Message);
            }
        }
        

        return "Not Found";
    }
    
}