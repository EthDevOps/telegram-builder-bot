using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Newtonsoft.Json;

namespace TelegramBuildBot;

public class Builder
{
    public Builder(string gitHubToken)
    {
        GitHubToken = gitHubToken;
    }
    
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
        {"mariusvanderwijden/tx-fuzz","build-push-tx-fuzz.yml"},
    };

    public string GitHubToken { get; set; }

    public async Task<string?> ProcessMessage(string messageText)
    {

        if (messageText.StartsWith("/build") || messageText.StartsWith("/barnabas"))
        {
            string[] msgSegments = messageText.Split(' ');
            if (msgSegments.Length < 2)
            {
                Console.WriteLine("No repo link supplied");
                return "You need to supply a repository link with a branch\\.";
            }

            string url = msgSegments[1];

            var regex = new Regex(@"https:\/\/github\.com\/(.*?)\/tree\/(.*)");
            var match = regex.Match(url);

            if (match is { Success: true, Groups.Count: 3 })
            {
                string? repo = match.Groups[1].Value;
                string branch = match.Groups[2].Value;
                (bool triggerSuccess, List<string> dockerImages, string runUrl) = await TriggerGitHubWorkflow(repo, branch);
                if (triggerSuccess)
                {
                    Console.WriteLine($"Build triggered for {repo}/{branch} [Run URL: {runUrl} | DockerImage: {String.Join(':', dockerImages)}]");
                    return $"Your build was triggered\\. [View run on GitHub]({runUrl})\nDocker Image\\(s\\) once run completed:\n{string.Join('\n', dockerImages.Select(x => $"`{x}`"))}";
                }

                Console.WriteLine("Unable to trigger build.");
                return "Sorry\\. Was unable to trigger your build\\. Likely the repository is not supported\\.";

            }

            Console.WriteLine("Unable to parse repo");
            return "Sorry\\. Was unable to get the repository and branch from the URL\\. Check your URL and try again\\.";
        }

        return null;
    }
    private async Task<(bool IsSuccessStatusCode, List<string> dockerImageUrls, string? runUrl)> TriggerGitHubWorkflow(string? repo, string branch)
    {
        
        // Register a runner with github
        using HttpClient client = new();
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Authorization = AuthenticationHeaderValue.Parse($"Bearer {GitHubToken}");
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("build-bot", "1")); 
        
        var requestData = new { 
            @ref = "master", // Ref of the build repo
            inputs = new { 
                repository = repo, 
                @ref = branch
            } 
        };

        bool isFork = false;
        
        // Check if given repo is a direct map
        if (!RepoToWorkflow.TryGetValue(repo.ToLower(), out string? workflowId))
        {
            Console.WriteLine($"Repo {repo} not in the workflow map. checking for fork.");
            
            // check for fork
            var forkResp = await client.GetAsync($"https://api.github.com/repos/{repo}");
            if (forkResp.IsSuccessStatusCode)
            {
                // Got repo meta
                string forkContent = await forkResp.Content.ReadAsStringAsync();
                JsonDocument forkJson = System.Text.Json.JsonSerializer.Deserialize<JsonDocument>(forkContent);
                
                // check if fork
                isFork = forkJson.RootElement.GetProperty("fork").GetBoolean();
                if (!isFork)
                {
                    // Not a fork - irgnore.
                    return (false, null, String.Empty)!;
                }

                string? parentRepo = forkJson.RootElement.GetProperty("parent").GetProperty("full_name").GetString();

                // Check if we have a workflow for the parent
                if (!RepoToWorkflow.TryGetValue(parentRepo.ToLower(), out workflowId))
                {
                    return (false,null, String.Empty)!;
                }

                Console.WriteLine($"Found valid parent repo at {parentRepo}");
            }
            else
            {
                // Unable to grab repo meta - return error
                return (false, null, String.Empty)!;
            }

        }
        
        // Build tag url

        // Grab the docker base from the workflow
        string dockerBase = Regex.Match(workflowId, @"build-push-(.+)\.yml").Groups[1].Value;
        List<string> dockerImageUrls = new();

        //string dockerhubPrefix = "{dockerhubPrefix}";
        string dockerhubPrefix = "";
        if (dockerBase == "prysm")
        {
            if (isFork)
            {
                string forkUser = repo.Split('/')[0];
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/prysm-beacon-chain:{forkUser}-{branch}");
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/prysm-validator:{forkUser}-{branch}");
            }
            else
            {
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/prysm-beacon-chain:{branch}");
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/prysm-validator:{branch}");
            }
        }
        else if (dockerBase == "nimbus-eth2")
        {
            if (isFork)
            {
                string forkUser = repo.Split('/')[0];
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/nimbus-eth2:{forkUser}-{branch}");
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/nimbus-validator-client:{forkUser}-{branch}");
            }
            else
            {
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/nimbus-eth2:{branch}");
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/nimbus-validator-client:{branch}");
            }
            
        }
        else
        {
            if (isFork)
            {
                string forkUser = repo.Split('/')[0];
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/{dockerBase}:{forkUser}-{branch}");
            }
            else
            {
                dockerImageUrls.Add($"{dockerhubPrefix}ethpandaops/{dockerBase}:{branch}");
            }
        }

        // Trigger job
        var content = new StringContent(
            JsonConvert.SerializeObject(requestData), 
            Encoding.UTF8, 
            "application/json");
        
        HttpResponseMessage response = await client.PostAsync(
            $"https://api.github.com/repos/ethpandaops/eth-client-docker-image-builder/actions/workflows/{workflowId}/dispatches", content);

        // Grab job url from GH
        await Task.Delay(1500);
        HttpResponseMessage runsResponse = await client.GetAsync(
            $"https://api.github.com/repos/ethpandaops/eth-client-docker-image-builder/actions/runs");
        
        string? runUrl = String.Empty;
        if(response.IsSuccessStatusCode)
        {
            string runsContent = await runsResponse.Content.ReadAsStringAsync();
            JsonDocument? responseObject = System.Text.Json.JsonSerializer.Deserialize<JsonDocument>(runsContent);
            if (responseObject != null)
            {
                var firstRun = responseObject.RootElement.GetProperty("workflow_runs").EnumerateArray().FirstOrDefault();
                runUrl = firstRun.GetProperty("html_url").GetString();
            }
        }
        
        return (response.IsSuccessStatusCode, dockerImageUrls, runUrl);
    }

}