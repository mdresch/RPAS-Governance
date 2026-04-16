using Microsoft.Extensions.DependencyInjection;
using RPAS.Governance.Client;
using RPAS.Governance.Client.DTOs;
using RPAS.Governance.Client.Exceptions;
using System.Net.Http.Json;
using System;
using System.Threading.Tasks;

namespace RemotePetitioner;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("==================================================");
        Console.WriteLine("RPAS SOVEREIGN VERIFICATION - CSR-42 OUT-OF-NETWORK");
        Console.WriteLine("==================================================");
        Console.WriteLine("Isolating Petitioner: Binary & Process Isolation");
        Console.WriteLine("Governance Endpoint: http://localhost:17200");
        Console.WriteLine("--------------------------------------------------");

        var services = new ServiceCollection();
        services.AddRpasGovernanceClient("http://localhost:17200");
        var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<IGovernanceClient>();
        using var http = new System.Net.Http.HttpClient();

        AuthorityTokenDto? validToken = null;

        // TEST 1: LEGAL PETITION
        Console.Write("[TEST 1] Legal BusinessCase Approval... ");
        try 
        {
            var result = await client.ApproveBusinessCaseAsync("test-bc-ratified", "Verified via Remote Petitioner (CSR-42 Proof).");
            if (result.Success && result.AuthorityToken != null)
            {
                validToken = result.AuthorityToken;
                Console.WriteLine("PASS (Token Issued: {0})", validToken.Id);
            }
            else Console.WriteLine("FAIL (No Token)");
        }
        catch (Exception ex) { Console.WriteLine("ERROR: {0}", ex.Message); }

        // TEST 2: LAW VIOLATION (Missing Justification)
        Console.Write("[TEST 2] Law Violation (Empty Justification)... ");
        try 
        {
            await client.ApproveBusinessCaseAsync("test-bc-ratified", "");
            Console.WriteLine("FAIL (Should have been blocked)");
        }
        catch (ArgumentException ex)
        {
            Console.WriteLine("PASS (Blocked by SDK: {0})", ex.Message.Split('.')[0]);
        }
        catch (Exception ex) { Console.WriteLine("ERROR: {0}", ex.GetType().Name); }

        if (validToken != null)
        {
            // TEST 3: TOPOLOGY VIOLATION (Mutation Attempt)
            Console.Write("[TEST 3] Topology Violation (Illegal Path)... ");
            try 
            {
                var mutationPayload = new { 
                    TokenId = validToken.Id.ToString(), 
                    TargetPath = "/system/etc/shadow" 
                };
                var response = await http.PostAsJsonAsync("http://localhost:17200/mutation/execute", mutationPayload);
                if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
                {
                    Console.WriteLine("PASS (Blocked by G6 Envelope)");
                }
                else Console.WriteLine("FAIL (Status: {0})", response.StatusCode);
            }
            catch (Exception ex) { Console.WriteLine("ERROR: {0}", ex.Message); }

            // TEST 4: REPLAY PROTECTION
            Console.Write("[TEST 4] Replay Protection (Second Use)... ");
            try 
            {
                var firstUse = new { 
                    TokenId = validToken.Id.ToString(), 
                    TargetPath = "/docs/ratified/final_report.pdf" 
                };
                
                // First use (Legal)
                var resp1 = await http.PostAsJsonAsync("http://localhost:17200/mutation/execute", firstUse);
                if (resp1.StatusCode != System.Net.HttpStatusCode.OK)
                {
                    Console.WriteLine("ERROR during setup: {0}", resp1.StatusCode);
                    return;
                }

                // Second use (Replay)
                var resp2 = await http.PostAsJsonAsync("http://localhost:17200/mutation/execute", firstUse);
                if (resp2.StatusCode == System.Net.HttpStatusCode.Conflict)
                {
                    Console.WriteLine("PASS (Replay Blocked)");
                }
                else Console.WriteLine("FAIL (Status: {0})", resp2.StatusCode);
            }
            catch (Exception ex) { Console.WriteLine("ERROR: {0}", ex.Message); }
        }

        Console.WriteLine("--------------------------------------------------");
        Console.WriteLine("CSR-42 Verification Complete.");
        Console.WriteLine("==================================================");
    }
}
