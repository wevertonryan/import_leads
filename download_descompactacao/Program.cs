using download;
using System.Collections;
using System.Text.RegularExpressions;
using System.Net.Http;

internal class Program
{

    private static async Task Main(string[] args)
    {
        string url = $"https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/{DateTime.Now.ToString("yyyy-MM")}/";
        string html = "";

        int attempt = 0;
        while (attempt <= 3)
        {
            try
            {
                using var client = new HttpClient();
                html = await client.GetStringAsync(url);
                break;
            }
            catch (Exception e)
            {
                attempt++;
                Console.WriteLine($"Tentativa {attempt} falhou: {e}");
                if (attempt >= 3)
                {
                    Console.WriteLine("Numero de Tentativas excedidas!");
                    return;
                }
            }
        }

        // Regex simples para capturar links que terminam em .zip
        var matches = Regex.Matches(html, @"href=""([^""]+\.zip)""");

        foreach (Match match in matches)
        {
            await ReceitaImporter.DownloadAndExtractAsync(match.Groups[1].Value);
        }
    }
}