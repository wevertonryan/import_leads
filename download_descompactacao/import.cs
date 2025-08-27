using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;

namespace download
{
    public static class ReceitaImporter
    {
        public static async Task DownloadAndExtractAsync(string arquive, int maxRetries = 3)
        {
            // Força TLS 1.2
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // Caminho da pasta de destino
            string arquivePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DadosReceita");
            Directory.CreateDirectory(arquivePath);

            // URL do arquivo ZIP
            string url = $"https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/2025-07/{arquive}";

            int attempt = 0;

            while (attempt < maxRetries)
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromMinutes(5); // Timeout maior para arquivos grandes

                    using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode(); // lança exceção se não for 200

                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var zip = new ZipArchive(stream);
                    zip.ExtractToDirectory(arquivePath, overwriteFiles: true); // sobrescreve arquivos existentes

                    Console.WriteLine("Download e extração concluídos com sucesso!");
                    return; // terminou sem erro, sai do método
                }
                catch (Exception ex)
                {
                    attempt++;
                    Console.WriteLine($"Tentativa {attempt} falhou: {ex.Message}");
                    if (attempt >= maxRetries)
                        throw; // ultrapassou número máximo de tentativas, repassa exceção
                    await Task.Delay(2000); // espera 2 segundos antes de tentar novamente
                }
            }
        }
    }
}