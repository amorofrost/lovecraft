using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace Lovecraft.TelegramBot
{
    public interface IBotHandler
    {
        Task HandleMessageAsync(Message msg, CancellationToken ct);

        Task HandlePhotoAsync(Message msg, CancellationToken ct);
    }
}
