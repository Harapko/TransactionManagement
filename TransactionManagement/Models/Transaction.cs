using System.ComponentModel.DataAnnotations;

namespace TransactionManagement.Models;

public class Transaction
{
    [Key]
    public string transaction_id { get; set; }
    public string name { get; set; }
    public string email { get; set; }
    public string amount { get; set; }
    public DateTimeOffset transaction_date { get; set; }
    public string client_location { get; set; }
}