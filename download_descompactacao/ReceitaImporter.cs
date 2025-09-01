using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
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
            string paste = "";
            foreach (char letter in arquive)
            {
                if (letter == '.' || ('0' <= letter && letter <= '9'))
                    break;
                paste += letter;
            }
            return paste;
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
                    byte[] byteBuffer = new byte[81920];

                    while ((entry = zipInput.GetNextEntry()) != null)
                    {
                        if (!entry.IsFile) continue;

                        string entryPath = Path.Combine(arquivePath, entry.Name);
                        Directory.CreateDirectory(Path.GetDirectoryName(entryPath)!);

                        using var fileStream = File.Create(entryPath);

                        int bytesRead;
                        while ((bytesRead = zipInput.Read(byteBuffer, 0, byteBuffer.Length)) > 0)
                        {
                            // Converte os bytes lidos do ISO-8859-1 para UTF-8
                            var utf8Bytes = System.Text.Encoding.UTF8.GetBytes(
                                System.Text.Encoding.GetEncoding("ISO-8859-1").GetString(byteBuffer, 0, bytesRead)
                            );
                            fileStream.Write(utf8Bytes, 0, utf8Bytes.Length);
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
