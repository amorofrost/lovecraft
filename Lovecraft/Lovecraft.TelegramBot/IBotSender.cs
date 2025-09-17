using System.Threading;
using System.Threading.Tasks;

namespace Lovecraft.TelegramBot
{
    public interface IBotSender
    {
        Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
    }
}
