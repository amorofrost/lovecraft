using System;

namespace Lovecraft.Common.DataContracts
{
    public class CreateUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public string AvatarUri { get; set; } = string.Empty;
        public long? TelegramUserId { get; set; }
        public string? TelegramUsername { get; set; }
        public string? TelegramAvatarFileId { get; set; }
        // Optional credentials for username/password authentication
        // If provided, the server should hash the password before storing.
        public string? Username { get; set; }
        public string? Password { get; set; }
    }
}
