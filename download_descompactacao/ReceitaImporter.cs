using ICSharpCode.SharpZipLib.Core;
using ICSharpCode.SharpZipLib.Zip;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        private static readonly Dictionary<string, string> header = new Dictionary<string, string>
        {
            {"Cnaes", "\"codigo\",\"descricao\"\n" },
            {"Empresas", "\"cnpjBase\",\"razaoSocial\",\"naturezaJuridica\",\"qualificacaoResponsavel\",\"capitalSocial\",\"porteEmpresa\",\"enteFederativo\"\n" },
            {"Estabelecimentos", "\"cnpjBase\",\"cnpjOrdem\",\"cnpjDV\",\"matrizFilial\",\"nomeFantasia\",\"situacaoCadastral\",\"dataSituacaoCadastral\",\"motivoSituacaoCadastral\",\"cidadeExterior\",\"pais\",\"dataInicioAtividade\",\"cnaePrincipal\",\"cnaeSecundario\",\"tipoLogradouro\",\"logradouro\",\"numero\",\"complemento\",\"bairro\",\"CEP\",\"UF\",\"municipio\",\"ddd1\",\"telefone1\",\"ddd2\",\"telefone2\",\"dddFAX\",\"FAX\",\"correioEletronico\",\"situacaoEspecial\",\"dataSituacaoEspecial\"\n" },
            {"Motivos", "\"codigo\",\"descricao\"\n" },
            {"Municipios", "\"codigo\",\"descricao\"\n" },
            {"Naturezas", "\"codigo\",\"descricao\"\n" },
            {"Paises", "\"codigo\",\"descricao\"\n" },
            {"Qualificacoes", "\"codigo\",\"descricao\"\n" },
            {"Simples", "\"cnpjBase\",\"opcaoDoSimples\",\"dataOpcaoDoSimples\",\"dataExclusaoDoSimples\",\"MEI\",\"dataOpcaoMEI\",\"dataExclusaoMei\"\n" },
            {"Socios", "\"cnpjBase\",\"identificadoSocio\",\"nomeSocio\",\"cnpjCpf\",\"qualificaoSocio\",\"dataEntradaSociedade\",\"pais\",\"representanteLegal\",\"nomeRepresentante\",\"qualificacaoResponsavel\",\"faixaEtaria\"\n" }
        };

        private static string PasteIdentifier(string arquive)
        {
            return new string(arquive
                .TakeWhile(c => c != '.' && !char.IsDigit(c))
                .ToArray());
        }

        public static async Task DownloadAndExtractAsync(string arquive, string url, int maxRetries = 3)
        {
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            string directoryName = PasteIdentifier(arquive);
            string arquivePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "DadosReceita", directoryName);
            Directory.CreateDirectory(arquivePath);

            string urlArquive = url + arquive;
            int attempt = 0;

            var isoEncoding = Encoding.GetEncoding("ISO-8859-1");
            var utf8Encoding = new UTF8Encoding(false);

            // Buffers compartilhados para a extração
            byte[] inputBuffer = new byte[81920];
            char[] charBuffer = new char[81920];
            byte[] outputBuffer = new byte[81920 * 2];

            while (attempt < maxRetries)
            {
                try
                {
                    await ProcessDownloadAndExtraction(urlArquive, arquivePath, arquive, isoEncoding, utf8Encoding, inputBuffer, charBuffer, outputBuffer, directoryName);
                    Console.WriteLine($" - {arquive} concluído com sucesso!");
                    return;
                }
                catch (Exception ex)
                {
                    attempt++;
                    Console.WriteLine($" - Tentativa {attempt} de {arquive} falhou: {ex.Message}");
                    if (attempt >= maxRetries)
                    {
                        throw;
                    }
                    await Task.Delay(2000);
                }
            }
        }

        private static async Task ProcessDownloadAndExtraction(string url, string destinationPath, string arquiveName, Encoding isoEncoding, Encoding utf8Encoding, byte[] inputBuffer, char[] charBuffer, byte[] outputBuffer, string headerReference)
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();

            using var zipStream = await response.Content.ReadAsStreamAsync();
            using var zipInput = new ZipInputStream(zipStream);

            ZipEntry entry;
            while ((entry = zipInput.GetNextEntry()) != null)
            {
                if (!entry.IsFile) continue;

                string entryPath = Path.Combine(destinationPath, entry.Name);

                // Proteção contra path traversal
                if (!Path.GetFullPath(entryPath).StartsWith(Path.GetFullPath(destinationPath) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"Caminho inválido no ZIP: {entry.Name}");
                }

                using var fileStream = File.Create(entryPath);
                byte[] headerBytes = utf8Encoding.GetBytes(header[headerReference]);
                await fileStream.WriteAsync(headerBytes, 0, headerBytes.Length);

                int bytesRead;
                while ((bytesRead = zipInput.Read(inputBuffer, 0, inputBuffer.Length)) > 0)
                {
                    int charsRead = isoEncoding.GetChars(inputBuffer, 0, bytesRead, charBuffer, 0);
                    for (int i = 0; i < charsRead; i++)
                    {
                        if (charBuffer[i] == ';')
                        {
                            charBuffer[i] = ',';
                        }
                    }
                    int utf8BytesCount = utf8Encoding.GetBytes(charBuffer, 0, charsRead, outputBuffer, 0);
                    await fileStream.WriteAsync(outputBuffer, 0, utf8BytesCount);
                }
            }
        }

        public static async Task Start()
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

            var stopwatch = new Stopwatch();
            stopwatch.Start();
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
            stopwatch.Stop();
            Console.WriteLine("Todos os downloads foram concluídos!");
            Console.WriteLine($"Tempo total de Download: {stopwatch.Elapsed.TotalSeconds:F2} segundos");
        }
    }
}