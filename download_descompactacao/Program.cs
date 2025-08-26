using download;
using System.Collections;
using System.Text.RegularExpressions;
using System.Net.Http;

internal class Program
{

    private static async Task Main(string[] args)
    {
        string url = $"https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/{DateTime.Now.ToString("yyyy-MM")}/";
        Console.WriteLine(url);
        using var client = new HttpClient();

        var html = await client.GetStringAsync(url);
 
        // Regex simples para capturar links que terminam em .zip
        var matches = Regex.Matches(html, @"href=""([^""]+\.zip)""");

        foreach (Match match in matches)
        {
            Console.WriteLine(match.Groups[1].Value);
        }

        //await ReceitaImporter.DownloadAndExtractAsync("Cnaes");
    }
}