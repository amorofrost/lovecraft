using System;

namespace Lovecraft.WebAPI.Repositories
{
    public class DuplicateTelegramUserIdException : Exception
    {
        public DuplicateTelegramUserIdException(long telegramUserId)
            : base($"A user with TelegramUserId {telegramUserId} already exists")
        {
        }
    }
}
