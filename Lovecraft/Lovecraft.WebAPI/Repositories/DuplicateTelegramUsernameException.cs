using System;

namespace Lovecraft.WebAPI.Repositories
{
    public class DuplicateTelegramUsernameException : Exception
    {
        public DuplicateTelegramUsernameException(string username)
            : base($"A user with TelegramUsername '{username}' already exists")
        {
        }
    }
}
