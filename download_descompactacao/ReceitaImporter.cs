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
    public static class ReceitaImporter
    {
        private static readonly HttpClient httpClient = new()
        {
            Timeout = TimeSpan.FromMinutes(10),
            DefaultRequestVersion = HttpVersion.Version20 // Melhor desempenho HTTP/2
        };
        private static readonly int MaxConcurrentDownloads = Math.Min(Environment.ProcessorCount, 8);
        private static readonly SemaphoreSlim downloadSemaphore = new(MaxConcurrentDownloads);
        private static readonly IMongoDatabase mongoDatabase = new MongoClient("mongodb://localhost:27017").GetDatabase("LeadSearch");

        private static readonly Encoding Latin1Encoding = Encoding.GetEncoding("ISO-8859-1");

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
            await DropAllCollectionsAsync();

            string baseUrl = "https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/2025-08/";
            string html;
            try
            {
                html = await RetryAsync(() => httpClient.GetStringAsync(baseUrl), 3);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Falha ao obter a lista de arquivos da URL base: {baseUrl}");
                Console.WriteLine($"Erro: {ex.Message}");
                return;
            }

            var zipMatches = Regex.Matches(html, @"href=""([^""]+\.zip)""");
            var tasks = new List<Task>();

            Console.WriteLine("|=====|  INICIANDO DOWNLOAD DOS DADOS |=====|");
            var sw = new Stopwatch();
            sw.Start();

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

            var collection = mongoDatabase.GetCollection<BsonDocument>(category);
            var expectedHeaders = headers[category];

            ZipEntry entry;
            while ((entry = zipInput.GetNextEntry()) != null)
            {
                if (!entry.IsFile) continue;

                using var reader = new StreamReader(zipInput, Latin1Encoding, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
                var batch = new List<BsonDocument>(capacity: 5000);

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
                    if (batch.Count >= 5000)
                    {
                        await collection.InsertManyAsync(batch).ConfigureAwait(false);
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                    await collection.InsertManyAsync(batch).ConfigureAwait(false);
            }
        }

        public static async Task DropAllCollectionsAsync()
        {
            Console.WriteLine("|=====| INICIANDO LIMPEZA DO BANCO DE DADOS |=====|");
            var collectionNamesCursor = await mongoDatabase.ListCollectionNamesAsync();
            var collectionNames = await collectionNamesCursor.ToListAsync();

            if (!collectionNames.Any())
            {
                Console.WriteLine("Nenhuma coleção encontrada para apagar.");
            }
            else
            {
                foreach (var collectionName in collectionNames)
                {
                    Console.WriteLine($" - Apagando coleção: {collectionName}");
                    await mongoDatabase.DropCollectionAsync(collectionName);
                }
            }
            Console.WriteLine("|=====| LIMPEZA DO BANCO DE DADOS CONCLUÍDA |=====|");
            Console.WriteLine();
        }

        private static async Task<T> RetryAsync<T>(Func<Task<T>> action, int maxAttempts, int delayMs = 2000)
        {
            List<Exception> exceptions = new();
            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    if (attempt < maxAttempts - 1)
                    {
                        await Task.Delay(delayMs * (int)Math.Pow(2, attempt)); // Backoff exponencial
                    }
                }
            }
            throw new AggregateException($"Falha após {maxAttempts} tentativas.", exceptions);
        }
    }
}