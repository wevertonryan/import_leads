using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using ICSharpCode.SharpZipLib.Zip;
using ICSharpCode.SharpZipLib.Core;


namespace download
{
    public static class ReceitaImporter
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
        public static async Task DownloadAndExtractAsync(string arquive, string url, int maxRetries = 3)
        {
            // Força TLS 1.2
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            // Caminho da pasta de destino
            string arquivePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DadosReceita", PasteIdentifier(arquive));
            Directory.CreateDirectory(arquivePath);

            // URL do arquivo ZIP
            string urlArquive = url + arquive;

            Console.WriteLine($"Baixando e Descompactando: {arquive}");

            int attempt = 0;
            while (attempt < maxRetries)
            {
                try
                {
                    using var client = new HttpClient();
                    client.Timeout = TimeSpan.FromMinutes(5); // Timeout maior para arquivos grandes

                    using var response = await client.GetAsync(urlArquive, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode(); // lança exceção se não for 200

                    using var zipStream = await response.Content.ReadAsStreamAsync();
                    using var zipInput = new ZipInputStream(zipStream);

                    ZipEntry entry;
                    byte[] buffer = new byte[81920]; // 80 KB por vez

                    // Itera pelos arquivos dentro do ZIP
                    while ((entry = zipInput.GetNextEntry()) != null)
                    {
                        if (!entry.IsFile)
                            continue; // pula diretórios

                        string entryPath = Path.Combine(arquivePath, entry.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);

                        using var fileStream = File.Create(entryPath);

                        // Copia os dados do arquivo em streaming
                        StreamUtils.Copy(zipInput, fileStream, buffer);
                    }


                    //using var zip = new ZipArchive(stream);
                    //zip.ExtractToDirectory(arquivePath, overwriteFiles: true); // sobrescreve arquivos existentes

                    Console.WriteLine(" - Download e extração concluídos com sucesso!\n");
                    return; // terminou sem erro, sai do método
                }
                catch (Exception ex)
                {
                    attempt++;
                    Console.WriteLine($" - Tentativa {attempt} falhou: {ex.Message}");
                    if (attempt >= maxRetries)
                        throw; // ultrapassou número máximo de tentativas, repassa exceção
                    await Task.Delay(2000); // espera 2 segundos antes de tentar novamente
                }
            }
        }
    }
}