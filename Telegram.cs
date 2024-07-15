using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace TelegramBuildBot;

internal static class Telegram
{
    public static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        // Only process Message updates: https://core.telegram.org/bots/api#message
        if (update.Message is not { } message)
            return;
        // Only process text messages
        if (message.Text is not { } messageText)
            return;

        long chatId = message.Chat.Id;
        Console.WriteLine($"Received a '{messageText}' message in chat {chatId} on Telegram.");
        
        string? responseMsg = await Program.Builder.ProcessMessage(messageText);
        if (responseMsg != null)
        {
            Console.WriteLine($"Chat response:\n{responseMsg}");
            try
            {
                bool isTopic = message.IsTopicMessage ?? false;
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: responseMsg,
                    parseMode: ParseMode.MarkdownV2,
                    messageThreadId: isTopic ? message.MessageThreadId : null,
                    replyToMessageId: message.MessageId,

                    cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Unable to send msg: {ex.Message}");
            }
        }
    }

    public static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        string errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]  {apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }
}