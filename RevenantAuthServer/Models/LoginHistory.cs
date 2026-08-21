namespace RevenantAuthServer.Models
{
    public class LoginHistory
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string? Ip { get; set; }
        public DateTime At { get; set; } = DateTime.UtcNow;
    }
}
