namespace CapacitorScanner.Api.Models
{
    public class UpdatePasswordRequestModel
    {
        public string NewPassword { get; set; } = string.Empty;
        public string OldPassword { get; set; } = string.Empty;
    }
}
