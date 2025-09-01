using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace download
{
    public static class ReceitaImporter
    {
        private static readonly HttpClient client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        private static readonly SemaphoreSlim semaphore = new SemaphoreSlim(3); // Limite de 3 downloads simultâneos

        private static string PasteIdentifier(string arquive)
        {
            return new string(arquive
                .TakeWhile(c => c != '.' && !char.IsDigit(c))
                .ToArray());
        }

        public static async Task DownloadAndExtractAsync(string arquive, string url, int maxRetries = 3)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;

            string arquivePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DadosReceita", PasteIdentifier(arquive));
            Directory.CreateDirectory(arquivePath);

            string urlArquive = url + arquive;

            int attempt = 0;
            while (attempt < maxRetries)
            {
                try
                {
                    using var response = await client.GetAsync(urlArquive, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();

                    using var zipStream = await response.Content.ReadAsStreamAsync();
                    using var zipInput = new ZipInputStream(zipStream);

                    ZipEntry entry;
                    byte[] inputBuffer = new byte[81920];           // Buffer de leitura do ZIP
                    char[] charBuffer = new char[81920];            // Buffer de chars ISO-8859-1
                    byte[] outputBuffer = new byte[81920 * 2];      // Buffer para UTF-8 (maior por segurança)

                    var isoEncoding = Encoding.GetEncoding("ISO-8859-1");
                    var utf8Encoding = new UTF8Encoding(false);

                    while ((entry = zipInput.GetNextEntry()) != null)
                    {
                        if (!entry.IsFile) continue;

                        string entryPath = Path.Combine(arquivePath, entry.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);

                        using var fileStream = File.Create(entryPath);

                        int bytesRead;
                        while ((bytesRead = zipInput.Read(inputBuffer, 0, inputBuffer.Length)) > 0)
                        {
                            // Converte diretamente os bytes ISO-8859-1 para chars
                            int charsRead = isoEncoding.GetChars(inputBuffer, 0, bytesRead, charBuffer, 0);

                            // Converte chars para UTF-8 bytes no buffer de saída
                            int utf8BytesCount = utf8Encoding.GetBytes(charBuffer, 0, charsRead, outputBuffer, 0);

                            // Escreve no arquivo final
                            fileStream.Write(outputBuffer, 0, utf8BytesCount);
                        }
                    }

                    Console.WriteLine($" - {arquive} concluído com sucesso!");
                    return;
                }
                catch (Exception ex)
                {
                    attempt++;
                    Console.WriteLine($" - Tentativa {attempt} de {arquive} falhou: {ex.Message}");
                    if (attempt >= maxRetries) throw;
                    await Task.Delay(2000);
                }
            }
        }


        public static async Task Agendamento()
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string url = "https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/2025-08/";

            string html = "";
            int attempt = 0;
            while (attempt < 3)
            {
                try
                {
                    html = await client.GetStringAsync(url);
                    break;
                }
                catch (Exception ex)
                {
                    attempt++;
                    Console.WriteLine($"Tentativa {attempt} falhou: {ex.Message}");
                    if (attempt >= 3)
                    {
                        Console.WriteLine("Número de tentativas excedidas!");
                        return;
                    }
                }
            }

            var matches = Regex.Matches(html, @"href=""([^""]+\.zip)""");
            var tasks = new List<Task>();

            Console.WriteLine("|=====|  INICIANDO DOWNLOAD DOS DADOS |=====|");

            foreach (Match match in matches)
            {
                string arquiveName = match.Groups[1].Value;
                await semaphore.WaitAsync(); // espera até ter permissão para iniciar download

                tasks.Add(Task.Run(async () =>
                {
                    try
                    {
                        await DownloadAndExtractAsync(arquiveName, url);
                    }
                    finally
                    {
                        semaphore.Release(); // libera para outro download
                    }
                }));
            }

            await Task.WhenAll(tasks); // aguarda todos os downloads terminarem
            Console.WriteLine("Todos os downloads foram concluídos!");
        }
    }
}
