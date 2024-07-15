using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using JsonDocument = System.Text.Json.JsonDocument;

namespace TelegramBuildBot;
internal class Program
{

    public static Builder Builder = null!;
    public static async Task Main(string[] args)
    {
        string telegramToken = Environment.GetEnvironmentVariable("TELEGRAM_TOKEN") ?? String.Empty;
        if (string.IsNullOrWhiteSpace(telegramToken))
        {
            Console.WriteLine("TELEGRAM_TOKEN not set. existing.");
            return;
        }
        
        string githubToken = Environment.GetEnvironmentVariable("GITHUB_TOKEN") ?? String.Empty;
        if (string.IsNullOrWhiteSpace(githubToken))
        {
            Console.WriteLine("GITHUB_TOKEN not set. existing.");
            return;
        }
        
        string discordToken = Environment.GetEnvironmentVariable("DISCORD_TOKEN") ?? String.Empty;
        if (string.IsNullOrWhiteSpace(discordToken))
        {
            Console.WriteLine("DISCORD_TOKEN not set. existing.");
            return;
        }
        

        Builder = new Builder(githubToken);
       
        // Set up Telegram
        TelegramBotClient botClient = new(telegramToken);
        using CancellationTokenSource cts = new ();

        // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
        ReceiverOptions receiverOptions = new ()
        {
            AllowedUpdates = [] // receive all update types except ChatMember related updates
        };

        botClient.StartReceiving(
            updateHandler: Telegram.HandleUpdateAsync,
            pollingErrorHandler: Telegram.HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
            
        );
        User me = await botClient.GetMeAsync(cancellationToken: cts.Token);
        Console.WriteLine($"Started listening on Telegram for @{me.Username}.");
        
        // Fire up discord
        DiscordBot discord = new();
        await discord.StartBot(discordToken);
        Console.WriteLine("Discord bot started...");
        
        
        // Bot is in threads - sleep main thread
        while (true)
        {
            Thread.Sleep(Timeout.Infinite);
        }

        // Send cancellation request to stop bot
        await cts.CancelAsync();
        Console.WriteLine("stopped.");

    }
}
