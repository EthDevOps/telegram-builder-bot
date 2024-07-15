using Discord;
using Discord.WebSocket;

namespace TelegramBuildBot;

public class DiscordBot
{
    private readonly DiscordSocketClient _client;

    public DiscordBot()
    {
        var settings = new DiscordSocketConfig();
        settings.GatewayIntents = GatewayIntents.AllUnprivileged | GatewayIntents.MessageContent;
        _client = new DiscordSocketClient(settings);
        
        _client.Log += LogAsync;
        _client.MessageReceived += MessageReceivedAsync;
    }

    private Task LogAsync(LogMessage log)
    {
        Console.WriteLine($"DISCORD: {log.ToString()}");
        return Task.CompletedTask;
    }

    private async Task MessageReceivedAsync(SocketMessage message)
    {
        if (message.Author.Id == _client.CurrentUser.Id || message.Author.IsBot)
            return;

        Console.WriteLine($"DISCORDMSG: {message.Content}");
        string? responseMsg = await Program.Builder.ProcessMessage(message.Content);
        
        if (responseMsg != null)
        {
            await message.Channel.SendMessageAsync(responseMsg);
        }
    }

    public async Task StartBot(string token)
    {
        await _client.LoginAsync(TokenType.Bot, token);
        await _client.StartAsync();

    }
}