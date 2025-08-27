using download;
using System.Collections;
using System.Text.RegularExpressions;
using System.Net.Http;

internal class Program
{
    private static string PasteIdentifier(string arquive)
    {
        string paste = "";
        foreach (char letter in arquive)
        {
            if (letter == '.' || ('0' <= letter && letter <= '9'))
            {
                break;
            }
            paste += letter;
        }
        return paste;
    }
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
            catch (Exception ex)
            {
                attempt++;
                Console.WriteLine($"Tentativa {attempt} falhou: {ex.Message}");
                if (attempt >= 3)
                {
                    Console.WriteLine("Numero de Tentativas excedidas!");
                    return;
                }
            }
        }

        // Regex simples para capturar links que terminam em .zip
        var matches = Regex.Matches(html, @"href=""([^""]+\.zip)""");

        Console.WriteLine("|=====|  INICIANDO DOWNLOAD DOS DADOS |=====| ");
        foreach (Match match in matches)
        {
            //Console.WriteLine(match.Groups[1].Value);
            await ReceitaImporter.DownloadAndExtractAsync(match.Groups[1].Value, url);
        }
    }
}