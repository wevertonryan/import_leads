using ICSharpCode.SharpZipLib.Zip;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace download_descompactacao
{
        public static class ReceitaImporter2
        {
            private static readonly HttpClient httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            private static readonly SemaphoreSlim downloadSemaphore = new SemaphoreSlim(2);
            private static readonly IMongoDatabase mongoDatabase = new MongoClient("mongodb://localhost:27017").GetDatabase("LeadSearch");

            private static readonly Dictionary<string, string[]> headers = new()
            {
                ["Cnaes"] = ["codigo", "descricao"],
                ["Empresas"] = ["cnpjBase", "razaoSocial", "naturezaJuridica", "qualificacaoResponsavel", "capitalSocial", "porteEmpresa", "enteFederativo"],
                ["Estabelecimentos"] = ["cnpjBase", "cnpjOrdem", "cnpjDV", "matrizFilial", "nomeFantasia", "situacaoCadastral", "dataSituacaoCadastral", "motivoSituacaoCadastral", "cidadeExterior", "pais", "dataInicioAtividade", "cnaePrincipal", "cnaeSecundario", "tipoLogradouro", "logradouro", "numero", "complemento", "bairro", "CEP", "UF", "municipio", "ddd1", "telefone1", "ddd2", "telefone2", "dddFAX", "FAX", "correioEletronico", "situacaoEspecial", "dataSituacaoEspecial"],
                ["Motivos"] = ["codigo", "descricao"],
                ["Municipios"] = ["codigo", "descricao"],
                ["Naturezas"] = ["codigo", "descricao"],
                ["Paises"] = ["codigo", "descricao"],
                ["Qualificacoes"] = ["codigo", "descricao"],
                ["Simples"] = ["cnpjBase", "opcaoDoSimples", "dataOpcaoDoSimples", "dataExclusaoDoSimples", "MEI", "dataOpcaoMEI", "dataExclusaoMei"],
                ["Socios"] = ["cnpjBase", "identificadoSocio", "nomeSocio", "cnpjCpf", "qualificaoSocio", "dataEntradaSociedade", "pais", "representanteLegal", "nomeRepresentante", "qualificacaoResponsavel", "faixaEtaria"]
            };

            private static string GetCategoryFromFilename(string fileName)
                => new string(fileName.TakeWhile(c => c != '.' && !char.IsDigit(c)).ToArray());

            public static async Task Start()
            {
                string baseUrl = "https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/2025-08/";
                string html = await RetryAsync(() => httpClient.GetStringAsync(baseUrl), 3);

                var zipMatches = Regex.Matches(html, @"href=""([^""]+\.zip)""");
                var tasks = new List<Task>();

                Console.WriteLine("|=====|  INICIANDO DOWNLOAD DOS DADOS |=====|");
                var sw = Stopwatch.StartNew();

                foreach (Match match in zipMatches)
                {
                    string fileName = match.Groups[1].Value;
                    string category = GetCategoryFromFilename(fileName);

                    if (!headers.ContainsKey(category)) continue;

                    await downloadSemaphore.WaitAsync();
                    tasks.Add(Task.Run(async () =>
                    {
                        try
                        {
                            await DownloadAndProcessFileAsync(baseUrl + fileName, category);
                            Console.WriteLine($" - {fileName} concluído com sucesso!");
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Erro ao processar {fileName}: {ex.Message}");
                        }
                        finally
                        {
                            downloadSemaphore.Release();
                        }
                    }));
                }

                await Task.WhenAll(tasks);
                sw.Stop();
                Console.WriteLine($"Todos os downloads concluídos em {sw.Elapsed.TotalSeconds:F2}s.");
            }

            private static async Task DownloadAndProcessFileAsync(string url, string category)
            {
                var response = await RetryAsync(() => httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead), 3);
                response.EnsureSuccessStatusCode();

                await using var zipStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var zipInput = new ZipInputStream(zipStream);
                zipInput.IsStreamOwner = false;

                var encoding = Encoding.GetEncoding("ISO-8859-1");
                var collection = mongoDatabase.GetCollection<BsonDocument>(category);
                var expectedHeaders = headers[category];

                ZipEntry entry;
                while ((entry = zipInput.GetNextEntry()) != null)
                {
                    if (!entry.IsFile) continue;

                    using var reader = new StreamReader(zipInput, encoding);
                    var batch = new List<BsonDocument>(capacity: 1000);

                    string? line;
                    while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                    {
                        if (string.IsNullOrWhiteSpace(line)) continue;

                        line = line.Replace("\"", "");
                        var values = line.Split(';');
                        if (values.Length != expectedHeaders.Length) continue;

                        var doc = new BsonDocument();
                        for (int i = 0; i < expectedHeaders.Length; i++)
                        {
                            doc[expectedHeaders[i]] = values[i].Trim();
                        }

                        batch.Add(doc);
                        if (batch.Count >= 1000)
                        {
                            await collection.InsertManyAsync(batch).ConfigureAwait(false);
                            batch.Clear();
                        }
                    }

                    if (batch.Count > 0)
                        await collection.InsertManyAsync(batch).ConfigureAwait(false);
                }
            }

            private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxAttempts, int delayMs = 2000)
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                int attempt = 0;
                while (true)
                {
                    try
                    {
                        return await action().ConfigureAwait(false);
                    }
                    catch when (++attempt < maxAttempts)
                    {
                        await Task.Delay(delayMs).ConfigureAwait(false);
                    }
                }
            }
        }
    }