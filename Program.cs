﻿using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Telegram.Bot;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Polling;
using Telegram.Bot.Types;

namespace TelegramBuildBot;
internal class Program
{
    private static readonly Dictionary<string, string?> RepoToWorkflow = new()
    {
        {"ethpandaops/armiarma","build-push-armiarma.yml"},
        {"dapplion/beacon-metrics-gazer","build-push-beacon-metrics-gazer.yml"},
        {"hyperledger/besu","build-push-besu.yml"},
        {"ralexstokes/ethereum_consensus_monitor","build-push-consensus-monitor.yml"},
        {"sigp/eleel","build-push-eleel.yml"},
        {"ledgerwatch/erigon","build-push-erigon.yml"},
        {"ethpandaops/ethereum-genesis-generator","build-push-genesis-generator.yml"},
        {"ethereumjs/ethereumjs-monorepo","build-push-ethereumjs.yml"},
        {"ethereum/nodemonitor","build-push-execution-monitor.yml"},
        {"flashbots/builder","build-push-flashbots-builder.yml"},
        {"ethereum/go-ethereum","build-push-geth.yml"},
        {"ethpandaops/goomy-blob","build-push-goomy-blob.yml"},
        {"migalabs/goteth","build-push-goteth.yml"},
        {"grandinetech/grandine","build-push-grandine.yml"},
        {"sigp/lighthouse","build-push-lighthouse.yml"},
        {"chainsafe/lodestar","build-push-lodestar.yml"},
        {"ralexstokes/mev-rs","build-push-mev-rs.yml"},
        {"nethermindeth/nethermind","build-push-nethermind.yml"},
        {"status-im/nimbus-eth1","build-push-nimbus-eth1.yml"},
        {"status-im/nimbus-eth2","build-push-nimbus-eth2.yml"},
        {"prysmaticlabs/prysm","build-push-prysm.yml"},
        {"paradigmxyz/reth","build-push-reth.yml"},
        {"consensys/teku","build-push-teku.yml"},
        {"MariusVanDerWijden/tx-fuzz","build-push-tx-fuzz.yml"},
    };

    private static string _gitHubToken = String.Empty;
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
        _gitHubToken = githubToken;
        
        TelegramBotClient botClient = new(telegramToken);
        using CancellationTokenSource cts = new ();

        // StartReceiving does not block the caller thread. Receiving is done on the ThreadPool.
        ReceiverOptions receiverOptions = new ()
        {
            AllowedUpdates = [] // receive all update types except ChatMember related updates
        };

        botClient.StartReceiving(
            updateHandler: HandleUpdateAsync,
            pollingErrorHandler: HandlePollingErrorAsync,
            receiverOptions: receiverOptions,
            cancellationToken: cts.Token
        );

        User me = await botClient.GetMeAsync(cancellationToken: cts.Token);

        Console.WriteLine($"Start listening for @{me.Username}. Stop with keypress..");
        Console.ReadLine();

        // Send cancellation request to stop bot
        await cts.CancelAsync();
        Console.WriteLine("stopped.");

    }
    static async Task HandleUpdateAsync(ITelegramBotClient botClient, Update update, CancellationToken cancellationToken)
    {
        // Only process Message updates: https://core.telegram.org/bots/api#message
        if (update.Message is not { } message)
            return;
        // Only process text messages
        if (message.Text is not { } messageText)
            return;

        long chatId = message.Chat.Id;

        Console.WriteLine($"Received a '{messageText}' message in chat {chatId}.");
        
        // check for command
        if (messageText.StartsWith("/build"))
        {
            string[] msgSegments = messageText.Split(' ');
            if (msgSegments.Length < 2)
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "You need to supply a repository link with a branch.",
                    cancellationToken: cancellationToken);
                Console.WriteLine("No repo link supplied");
                return;

            }
            string url = msgSegments[1];
            
            var regex = new Regex(@"https:\/\/github\.com\/(.*?)\/tree\/(.*)");
            var match = regex.Match(url);

            if (match is { Success: true, Groups.Count: 3 })
            {
                string repo = match.Groups[1].Value;
                string branch = match.Groups[2].Value;
                if (await TriggerGitHubWorkflow(_gitHubToken, repo, branch))
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Your build was triggered.",
                        cancellationToken: cancellationToken);
                    Console.WriteLine($"Build triggered for {repo} - {branch}"); 
                }
                else
                {
                    await botClient.SendTextMessageAsync(
                        chatId: chatId,
                        text: "Sorry. Was unable to trigger your build. Likely the repository is not supported.",
                        cancellationToken: cancellationToken);
                    Console.WriteLine("Unable to trigger build."); 
                }
                
            }
            else
            {
                await botClient.SendTextMessageAsync(
                    chatId: chatId,
                    text: "Sorry. Was unable to get the repository and branch from the URL. Check your URL and try again.",
                    cancellationToken: cancellationToken);
                Console.WriteLine("Unable to parse repo");
            }
        }
    }

    private static async Task<bool> TriggerGitHubWorkflow(string gitHubToken, string repo, string branch)
    {
        
        // Register a runner with github
        using HttpClient client = new();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {gitHubToken}");
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("build-bot", "1")); 
        
        var requestData = new { 
            @ref = "master", // Ref of the build repo
            inputs = new { 
                repository = repo, 
                @ref = branch
            } 
        };

        if (!RepoToWorkflow.TryGetValue(repo, out string? workflowId))
        {
            Console.WriteLine($"Repo {repo} not in the workflow map. ignoring.");
            return false;
        }
        
        var content = new StringContent(
            JsonConvert.SerializeObject(requestData), 
            Encoding.UTF8, 
            "application/json");
        
        HttpResponseMessage response = await client.PostAsync(
            $"https://api.github.com/repos/ethpandaops/eth-client-docker-image-builder/actions/workflows/{workflowId}/dispatches", content);

        return response.IsSuccessStatusCode;
    }

    static Task HandlePollingErrorAsync(ITelegramBotClient botClient, Exception exception, CancellationToken cancellationToken)
    {
        string errorMessage = exception switch
        {
            ApiRequestException apiRequestException
                => $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
            _ => exception.ToString()
        };

        Console.WriteLine(errorMessage);
        return Task.CompletedTask;
    }
}