using System.Threading;
using System.Threading.Tasks;

namespace Lovecraft.TelegramBot
{
    public interface IBotSender
    {
        Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
        Task SendPhotoAsync(long chatId, string fileId, string? caption = null, CancellationToken cancellationToken = default);
    }
}
