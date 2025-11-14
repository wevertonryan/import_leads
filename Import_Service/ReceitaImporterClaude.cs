using ICSharpCode.SharpZipLib.Zip;
using MongoDB.Bson;
using MongoDB.Driver;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;

namespace Import_Service
{
    public static class ReceitaImporterClaude
    {
        // ==================== CONFIGURAÇÕES ====================

        private static readonly Dictionary<string, int> ComputerConfig = new()
        {
            ["Cores"] = Environment.ProcessorCount,
            ["RAM"] = 0,
            ["Disk"] = 0
        };

        private static readonly Dictionary<string, string> ConnectionDatabaseConfig = new()
        {
            ["DatabaseName"] = "LeadSearch",
            ["ConnectionString"] = "mongodb://localhost:27017"
        };

        private static readonly string[] filesArray =
        [
            "Empresas8", "Empresas9", "Simples", "Socios7", "Socios8", "Socios9"
        ];

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

        // ==================== RECURSOS COMPARTILHADOS ====================

        private static readonly SemaphoreSlim downloadSemaphore = new(3);
        private static readonly IMongoDatabase mongoDatabase = new MongoClient(
            ConnectionDatabaseConfig["ConnectionString"]
        ).GetDatabase(ConnectionDatabaseConfig["DatabaseName"]);

        private static readonly HttpClient httpClient = new(new HttpClientHandler
        {
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
            DefaultRequestVersion = HttpVersion.Version20
        };

        private static readonly Encoding Latin1Encoding = Encoding.GetEncoding("ISO-8859-1");
        private static string baseUrl = "https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/2025-09/";

        // Estatísticas globais
        private static readonly ConcurrentDictionary<string, FileStats> fileStats = new();

        // ==================== MÉTODO PRINCIPAL ====================

        public static async Task Start()
        {
            Console.WriteLine("╔════════════════════════════════════════════╗");
            Console.WriteLine("║    Iniciando ReceitaImporter v2.0         ║");
            Console.WriteLine("╚════════════════════════════════════════════╝");
            Console.WriteLine();

            // Verifica conexões
            if (!await CheckMongoConnection() || !await CheckHttpConnection())
            {
                Console.WriteLine("❌ Falha nas verificações de conexão. Abortando.");
                return;
            }

            await DropAllCollectionsAsync();

            var globalSw = Stopwatch.StartNew();
            var processFiles = new List<Task>();

            foreach (var fileName in filesArray)
            {
                await downloadSemaphore.WaitAsync();
                processFiles.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessFile(fileName);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"❌ Erro fatal ao processar {fileName}: {ex.Message}");
                        LogException(fileName, ex);
                    }
                    finally
                    {
                        downloadSemaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(processFiles);
            globalSw.Stop();

            PrintFinalReport(globalSw.Elapsed);
        }

        // ==================== PROCESSAMENTO DE ARQUIVO ====================

        private static async Task ProcessFile(string fileName)
        {
            var stats = new FileStats { FileName = fileName };
            fileStats[fileName] = stats;

            var file = baseUrl + fileName + ".zip";
            var config = CalculateOptimalWorkerConfig();

            // Canais com capacidade otimizada
            var channelOptions = new BoundedChannelOptions(config.ChannelCapacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = false,
                SingleWriter = false
            };

            var dataDownload = Channel.CreateBounded<PooledByteArray>(channelOptions);
            var dataProcess = Channel.CreateBounded<BsonDocument>(channelOptions);

            var cts = new CancellationTokenSource();
            var sw = Stopwatch.StartNew();

            try
            {
                // Download com retry
                var response = await RetryAsync(
                    async () => await httpClient.GetAsync(file, HttpCompletionOption.ResponseHeadersRead),
                    maxAttempts: 3
                );

                if (!response.IsSuccessStatusCode)
                {
                    throw new Exception($"Falha no download: {response.StatusCode}");
                }

                // Remove números do fileName para pegar headers
                var cleanFileName = Regex.Replace(fileName, @"[0-9]", "");
                var fileHeaders = headers[cleanFileName];
                var collection = mongoDatabase.GetCollection<BsonDocument>(cleanFileName);
                var insertOpts = new InsertManyOptions { IsOrdered = false };

                // Inicia workers
                var tasks = new List<Task>();

                // 1 Downloader
                tasks.Add(StartDownloader(response, dataDownload.Writer, config, stats, cts.Token));

                // N Processors
                for (int i = 0; i < config.ProcessorCount; i++)
                {
                    tasks.Add(StartProcessor(
                        dataDownload.Reader,
                        dataProcess.Writer,
                        fileHeaders,
                        stats,
                        cts.Token
                    ));
                }

                // Aguarda download e processamento
                await Task.WhenAll(tasks);
                dataProcess.Writer.Complete();

                // N Importers
                var importerTasks = new List<Task>();
                for (int i = 0; i < config.ImporterCount; i++)
                {
                    importerTasks.Add(StartImporter(
                        dataProcess.Reader,
                        collection,
                        config.BatchSize,
                        insertOpts,
                        stats,
                        cts.Token
                    ));
                }

                await Task.WhenAll(importerTasks);

                sw.Stop();
                stats.TotalTime = sw.Elapsed;
                stats.Success = true;

                Console.WriteLine($"✓ {fileName} concluído em {sw.Elapsed:hh\\:mm\\:ss} | " +
                                $"{stats.DocumentsInserted:N0} docs | " +
                                $"{stats.DocumentsInserted / sw.Elapsed.TotalSeconds:F0} docs/s");
            }
            catch (Exception ex)
            {
                sw.Stop();
                stats.Success = false;
                stats.ErrorMessage = ex.Message;
                Console.WriteLine($"❌ {fileName} falhou após {sw.Elapsed:hh\\:mm\\:ss}: {ex.Message}");
                cts.Cancel();
                throw;
            }
            finally
            {
                cts.Dispose();
            }
        }

        // ==================== WORKERS ====================

        private static async Task StartDownloader(
            HttpResponseMessage response,
            ChannelWriter<PooledByteArray> writer,
            WorkerConfig config,
            FileStats stats,
            CancellationToken ct)
        {
            try
            {
                await using var zipStream = await response.Content.ReadAsStreamAsync(ct);
                using var zipInputStream = new ZipInputStream(zipStream) { IsStreamOwner = false };

                ZipEntry entry;
                while ((entry = zipInputStream.GetNextEntry()) != null)
                {
                    if (!entry.IsFile) continue;

                    stats.CurrentFile = entry.Name;
                    Console.WriteLine($"📥 {stats.FileName} → Lendo: {entry.Name}");

                    byte[] buffer = ArrayPool<byte>.Shared.Rent(config.BufferSize);
                    int remaining = 0;

                    try
                    {
                        int bytesRead;
                        while ((bytesRead = await zipInputStream.ReadAsync(
                            buffer.AsMemory(remaining, config.BufferSize - remaining), ct)) > 0)
                        {
                            int totalInBuffer = remaining + bytesRead;
                            int lastNewline = buffer.AsSpan(0, totalInBuffer).LastIndexOf((byte)'\n');

                            if (lastNewline >= 0)
                            {
                                int chunkSize = lastNewline + 1;

                                byte[] chunk = ArrayPool<byte>.Shared.Rent(chunkSize);
                                buffer.AsSpan(0, chunkSize).CopyTo(chunk);

                                await writer.WriteAsync(new PooledByteArray(chunk, chunkSize), ct);
                                stats.BytesDownloaded += chunkSize;

                                remaining = totalInBuffer - chunkSize;
                                if (remaining > 0)
                                {
                                    buffer.AsSpan(chunkSize, remaining).CopyTo(buffer);
                                }
                            }
                            else
                            {
                                remaining = totalInBuffer;
                            }
                        }

                        // Processa bytes restantes
                        if (remaining > 0)
                        {
                            byte[] chunk = ArrayPool<byte>.Shared.Rent(remaining);
                            buffer.AsSpan(0, remaining).CopyTo(chunk);
                            await writer.WriteAsync(new PooledByteArray(chunk, remaining), ct);
                            stats.BytesDownloaded += remaining;
                        }
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(buffer);
                    }
                }
            }
            finally
            {
                writer.Complete();
            }
        }

        private static async Task StartProcessor(
            ChannelReader<PooledByteArray> reader,
            ChannelWriter<BsonDocument> writer,
            string[] fileHeaders,
            FileStats stats,
            CancellationToken ct)
        {
            try
            {
                await foreach (var pooledArray in reader.ReadAllAsync(ct))
                {
                    try
                    {
                        // Processa TUDO sincronamente antes de fazer await
                        var documents = ProcessChunkToDocuments(pooledArray, fileHeaders, stats);

                        // Agora sim faz await para escrever os documentos
                        foreach (var doc in documents)
                        {
                            await writer.WriteAsync(doc, ct);
                        }
                    }
                    finally
                    {
                        pooledArray.Dispose();
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelamento normal
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Processor error em {stats.FileName}: {ex.Message}");
                throw;
            }
        }

        private static List<BsonDocument> ProcessChunkToDocuments(
            PooledByteArray pooledArray,
            string[] fileHeaders,
            FileStats stats)
        {
            var documents = new List<BsonDocument>();
            ReadOnlySpan<byte> span = pooledArray.AsSpan();
            int start = 0;

            while (start < span.Length)
            {
                int newline = span.Slice(start).IndexOf((byte)'\n');
                if (newline < 0) break;

                ReadOnlySpan<byte> line = span.Slice(start, newline);

                // Remove \r se existir
                if (line.Length > 0 && line[^1] == (byte)'\r')
                    line = line.Slice(0, line.Length - 1);

                if (line.Length > 0)
                {
                    try
                    {
                        var doc = ParseCsvLine(line, fileHeaders);
                        if (doc != null && doc.ElementCount > 0)
                        {
                            documents.Add(doc);
                            stats.LinesProcessed++;
                        }
                    }
                    catch
                    {
                        stats.ParseErrors++;
                        // Log silencioso - não trava o processo
                    }
                }

                start += newline + 1;
            }

            return documents;
        }

        private static BsonDocument ParseCsvLine(ReadOnlySpan<byte> line, string[] fileHeaders)
        {
            var doc = new BsonDocument();
            int fieldIndex = 0;
            int start = 0;
            bool inQuotes = false;

            for (int i = 0; i < line.Length; i++)
            {
                byte b = line[i];

                if (b == (byte)'"')
                {
                    inQuotes = !inQuotes;
                }
                else if (b == (byte)';' && !inQuotes)
                {
                    AddField(line.Slice(start, i - start), fieldIndex++);
                    start = i + 1;
                }
            }

            // Último campo
            if (start < line.Length)
            {
                AddField(line.Slice(start), fieldIndex);
            }

            return doc;

            void AddField(ReadOnlySpan<byte> field, int idx)
            {
                if (idx >= fileHeaders.Length) return;

                // Remove aspas iniciais e finais
                if (field.Length >= 2 && field[0] == (byte)'"' && field[^1] == (byte)'"')
                    field = field.Slice(1, field.Length - 2);

                if (field.Length > 0)
                {
                    string value = Latin1Encoding.GetString(field);

                    // Remove aspas duplas escapadas
                    if (value.Contains("\"\""))
                        value = value.Replace("\"\"", "\"");

                    doc[fileHeaders[idx]] = value;
                }
            }
        }

        private static async Task StartImporter(
            ChannelReader<BsonDocument> reader,
            IMongoCollection<BsonDocument> collection,
            int batchSize,
            InsertManyOptions opts,
            FileStats stats,
            CancellationToken ct)
        {
            var batch = new List<BsonDocument>(batchSize);

            try
            {
                await foreach (var doc in reader.ReadAllAsync(ct))
                {
                    batch.Add(doc);

                    if (batch.Count >= batchSize)
                    {
                        await collection.InsertManyAsync(batch, opts, ct);
                        stats.DocumentsInserted += batch.Count;
                        batch.Clear();

                        // Feedback periódico
                        if (stats.DocumentsInserted % 100000 == 0)
                        {
                            Console.WriteLine($"  💾 {stats.FileName}: {stats.DocumentsInserted:N0} docs inseridos");
                        }
                    }
                }

                // Insere lote final
                if (batch.Count > 0)
                {
                    await collection.InsertManyAsync(batch, opts, ct);
                    stats.DocumentsInserted += batch.Count;
                }
            }
            catch (OperationCanceledException)
            {
                // Cancelamento normal
            }
            catch (Exception ex)
            {
                Console.WriteLine($"⚠️ Importer error em {stats.FileName}: {ex.Message}");
                throw;
            }
        }

        // ==================== CONFIGURAÇÃO DINÂMICA ====================

        private static WorkerConfig CalculateOptimalWorkerConfig()
        {
            int availableCores = Environment.ProcessorCount;

            return new WorkerConfig
            {
                ProcessorCount = Math.Max(2, Math.Min(availableCores - 2, 6)),
                ImporterCount = 2,
                BatchSize = 5000,
                BufferSize = 64 * 1024, // 64KB
                ChannelCapacity = 10000
            };
        }

        // ==================== UTILITÁRIOS ====================

        private static async Task DropAllCollectionsAsync()
        {
            Console.WriteLine("🗑️  Limpando banco de dados...");

            var collectionNamesCursor = await mongoDatabase.ListCollectionNamesAsync();
            var collectionNames = await collectionNamesCursor.ToListAsync();

            if (!collectionNames.Any())
            {
                Console.WriteLine("  ℹ️  Nenhuma coleção encontrada");
            }
            else
            {
                foreach (var collectionName in collectionNames)
                {
                    Console.WriteLine($"  🗑️  Removendo: {collectionName}");
                    await mongoDatabase.DropCollectionAsync(collectionName);
                }
            }

            Console.WriteLine("✓ Limpeza concluída");
            Console.WriteLine();
        }

        public static async Task<bool> CheckMongoConnection()
        {
            try
            {
                var settings = MongoClientSettings.FromConnectionString(
                    ConnectionDatabaseConfig["ConnectionString"]
                );
                settings.ConnectTimeout = TimeSpan.FromSeconds(5);
                settings.SocketTimeout = TimeSpan.FromSeconds(5);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);

                var testDb = new MongoClient(settings).GetDatabase(
                    ConnectionDatabaseConfig["DatabaseName"]
                );

                await testDb.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
                Console.WriteLine("✓ Conexão MongoDB estabelecida");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro MongoDB: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> CheckHttpConnection()
        {
            try
            {
                var response = await httpClient.GetAsync(baseUrl);
                Console.WriteLine("✓ Conexão HTTP estabelecida");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Erro HTTP: {ex.Message}");
                return false;
            }
        }

        private static async Task<T> RetryAsync<T>(
            Func<Task<T>> action,
            int maxAttempts,
            int baseDelayMs = 2000)
        {
            var exceptions = new List<Exception>();

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
                        int delay = baseDelayMs * (int)Math.Pow(2, attempt);
                        Console.WriteLine($"⚠️ Tentativa {attempt + 1}/{maxAttempts} falhou. " +
                                        $"Retry em {delay}ms...");
                        await Task.Delay(delay);
                    }
                }
            }

            throw new AggregateException(
                $"Operação falhou após {maxAttempts} tentativas",
                exceptions
            );
        }

        private static void LogException(string fileName, Exception ex)
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "errors.log");
            var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {fileName}: {ex}\n\n";
            File.AppendAllText(logPath, logEntry);
        }

        private static void PrintFinalReport(TimeSpan totalTime)
        {
            Console.WriteLine();
            Console.WriteLine("╔════════════════════════════════════════════╗");
            Console.WriteLine("║         RELATÓRIO FINAL DE IMPORTAÇÃO      ║");
            Console.WriteLine("╚════════════════════════════════════════════╝");
            Console.WriteLine();
            Console.WriteLine($"⏱️  Tempo Total: {totalTime:hh\\:mm\\:ss}");
            Console.WriteLine();

            long totalDocs = 0;
            long totalBytes = 0;
            int successCount = 0;

            foreach (var kvp in fileStats.OrderBy(x => x.Key))
            {
                var stats = kvp.Value;
                var status = stats.Success ? "✓" : "❌";

                Console.WriteLine($"{status} {stats.FileName}:");
                Console.WriteLine($"   Documentos: {stats.DocumentsInserted:N0}");
                Console.WriteLine($"   Tempo: {stats.TotalTime:hh\\:mm\\:ss}");
                Console.WriteLine($"   Velocidade: {stats.DocumentsInserted / stats.TotalTime.TotalSeconds:F0} docs/s");

                if (stats.ParseErrors > 0)
                    Console.WriteLine($"   ⚠️ Erros de parse: {stats.ParseErrors:N0}");

                if (!stats.Success)
                    Console.WriteLine($"   Erro: {stats.ErrorMessage}");

                Console.WriteLine();

                if (stats.Success)
                {
                    totalDocs += stats.DocumentsInserted;
                    totalBytes += stats.BytesDownloaded;
                    successCount++;
                }
            }

            Console.WriteLine("═══════════════════════════════════════════");
            Console.WriteLine($"📊 Total de Documentos: {totalDocs:N0}");
            Console.WriteLine($"📦 Total de Bytes: {totalBytes / (1024.0 * 1024.0):F2} MB");
            Console.WriteLine($"✓ Arquivos bem-sucedidos: {successCount}/{fileStats.Count}");
            Console.WriteLine($"⚡ Velocidade média: {totalDocs / totalTime.TotalSeconds:F0} docs/s");
            Console.WriteLine("═══════════════════════════════════════════");
        }

        // ==================== CLASSES AUXILIARES ====================

        private class WorkerConfig
        {
            public int ProcessorCount { get; set; }
            public int ImporterCount { get; set; }
            public int BatchSize { get; set; }
            public int BufferSize { get; set; }
            public int ChannelCapacity { get; set; }
        }

        private class FileStats
        {
            public string FileName { get; set; } = "";
            public string CurrentFile { get; set; } = "";
            public long BytesDownloaded { get; set; }
            public long LinesProcessed { get; set; }
            public long DocumentsInserted { get; set; }
            public long ParseErrors { get; set; }
            public TimeSpan TotalTime { get; set; }
            public bool Success { get; set; }
            public string ErrorMessage { get; set; } = "";
        }

        private readonly struct PooledByteArray : IDisposable
        {
            private readonly byte[] _array;
            public readonly int Length;

            public PooledByteArray(byte[] array, int length)
            {
                _array = array;
                Length = length;
            }

            public ReadOnlySpan<byte> AsSpan() => _array.AsSpan(0, Length);

            public ReadOnlyMemory<byte> AsMemory() => _array.AsMemory(0, Length);

            public void Dispose()
            {
                if (_array != null)
                    ArrayPool<byte>.Shared.Return(_array);
            }
        }
    }
}