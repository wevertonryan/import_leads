using ICSharpCode.SharpZipLib.Zip;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using SharpCompress.Common;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace Import_Service
{
    public static class ReceitaImporter
    {
        private static readonly Dictionary<string, int> ComputerConfig = new()
        {
            ["Cores"] = Environment.ProcessorCount,
            ["RAM"] = 0,
            ["Disk"] = 0
        };
        //Configurações para conexão com o MongoDB
        //OBS: posteriormente não ficará aqui por questões de segurança
        private static readonly Dictionary<string, string> ConnectionDatabaseConfig = new()
        {
            ["DatabaseName"] = "LeadSearch",
            ["ConnectionString"] = "mongodb://localhost:27017"
        };
        private static readonly SemaphoreSlim downloadSemaphore = new(3);
        private static readonly string[] filesArray = ["Cnaes", /*"Empresas0", "Empresas1", "Empresas2", "Empresas3", "Empresas4","Empresas5", "Empresas6", "Empresas7","Empresas8","Empresas9","Estabelecimentos0","Estabelecimentos1","Estabelecimentos2","Estabelecimentos3","Estabelecimentos4","Estabelecimentos5","Estabelecimentos6","Estabelecimentos7","Estabelecimentos8","Estabelecimentos9", */"Motivos","Municipios","Naturezas","Paises","Qualificacoes","Simples",/*"Socios0","Socios1","Socios2","Socios3","Socios4","Socios5","Socios6","Socios7","Socios8",*/"Socios9"];
        //Conexão com o MongoDB
        private static readonly IMongoDatabase mongoDatabase = new MongoClient(ConnectionDatabaseConfig["ConnectionString"]).GetDatabase(ConnectionDatabaseConfig["DatabaseName"]);
        private static readonly HttpClient httpClient = new(new HttpClientHandler
        {
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12
        })
        {
            Timeout = TimeSpan.FromMinutes(10),
            DefaultRequestVersion = HttpVersion.Version20 // Melhor desempenho HTTP/2
        };
        private static readonly Encoding Latin1Encoding = Encoding.GetEncoding("ISO-8859-1");

        private static string baseUrl = "https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/2025-09/";
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

        /* CÓDIGO (Métodos Públicos)
           * Start(): Começa do Zero ou Continua de onde parou
           * Pause(): Pausa todo o Processo
           * Restart(): Começa tudo de novo
           * Cancel(): Cancela a execução do Processo
           * Progress(): Devolve um objeto/dicionario com o progresso da execução até o momento (Arquivos baixados, Tempo Decorrido até o momento)
           * Log(): Retorna as Mensagens de Erro
           * Config(): Para configurar alguma coisa da importação (ConnectionDatabaseConfig, Limitação dos recursos disponíveis, etc...)
         */
        public static async Task Start() 
        /* [Start]
        - Será o Main dessa Classe
        - Terá como Papel chamar os metodos para executar o processo de todos os arquivos */
        {
            Console.WriteLine("# Iniciando ReceitaImporter #");
            /*if (!(await CheckMongoConnection()))
            {
                return;
            }*/
            await DropAllCollectionsAsync();
            var processFiles = new List<Task>();
            var sw = new Stopwatch();
            sw.Start();
            foreach(var fileName in filesArray){
                await downloadSemaphore.WaitAsync();
                processFiles.Add(Task.Run(async () =>
                {
                    try
                    {
                        await ProcessFile(fileName);
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

            await Task.WhenAll(processFiles);
            sw.Stop();
            Console.WriteLine("# ReceitaImporter Finalizado #");
            Console.WriteLine($"Tempo Decorrido: {sw.Elapsed.Hours}h {sw.Elapsed.Minutes}m {sw.Elapsed.Seconds}s {sw.Elapsed.Milliseconds}ms");
        }

        private static async Task ProcessFile(string fileName)
        /* [ProcessDocument]
        - Método Responsavel pelo processo completo (download -> processamento -> importação) de um único
        - Também é onde ficarão os Canais (Channel)
        - Fará a Chamada dos Produtores e Consumidores 
        - Fará o Controle dinámico dos produtores e consumidores com base nos recursos disponiveis durante a execução (adição ou retiragem)
        - Adicionar/Retirar trabalhadores com base na necessidade, se quem estiver causando o gargalo for o banco adicionar mais no banco*/
        {
            var file = baseUrl + fileName + ".zip";
            var channelOptions = new BoundedChannelOptions(150) { FullMode = BoundedChannelFullMode.Wait }; //quando encher, começar a escrever no disco para não atrapalhar o download, ou alguma outra etapa

            var DataDownload = Channel.CreateBounded<ReadOnlyMemory<byte>>(channelOptions);
            var DataProcess = Channel.CreateBounded<BsonDocument>(channelOptions);

            ICollection<Task> downloaders = new List<Task>();
            ICollection<Task> processors = new List<Task>();
            ICollection<Task> importers = new List<Task>();
            List<byte[]> Chunks = new();
            const int batchSize = 5000;

            const int BufferSize = 8 * 1024; // 8KB por leitura
            byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

            var sw = new Stopwatch();
            sw.Start();
            HttpResponseMessage response;
            while (true)
            {
                try
                {
                    response = await httpClient.GetAsync(file, HttpCompletionOption.ResponseHeadersRead);
                    response.EnsureSuccessStatusCode();
                    break;
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }
            
            for (int i = 0; i < 1; i++)
            {
                var localRaw = Channel.CreateBounded<byte[]>(channelOptions);
                _ = Downloader(localRaw.Writer);
                downloaders.Add(LineSplitter(localRaw.Reader, DataDownload.Writer));
            }

            fileName = Regex.Replace(fileName, @"[0-9]", "");
            var thisHeaderCollection = headers[fileName];
            for (int i = 0; i < 8; i++)
            {
                processors.Add(Processor(DataDownload.Reader, DataProcess.Writer));
            }

            var opts = new InsertManyOptions { IsOrdered = false };
            var collection = mongoDatabase.GetCollection<BsonDocument>(fileName);
            for (int i = 0; i < 2; i++)
            {
                importers.Add(Importer(DataProcess.Reader));
            }
            await Task.WhenAll(downloaders);
            DataDownload.Writer.Complete();
            await Task.WhenAll(processors);
            DataProcess.Writer.Complete();
            await Task.WhenAll(importers);
            sw.Stop();
            Console.WriteLine($"Arquivo {fileName} Importado com Sucesso em {sw.Elapsed.Hours}h {sw.Elapsed.Minutes}m {sw.Elapsed.Seconds}s {sw.Elapsed.Milliseconds}ms!");

            async Task Downloader(ChannelWriter<byte[]> writer)
            /* [Downloader]
            - Produtor do Canal DataDownload
            - Irá realizar o download dos arquivos armazenando na RAM em Stream
            - Irá extrair
            - Ele irá armazenar em blocos (bytes), e irá adicionar esse blocos no Canal DataDownload
            - Terá provavelmente apenas 1, para baixar o arquivo inteiro, ou mais para realizar o download em partes 
            (só terá mais se tiver mais recurso disponivel mesmo baixando 3 arquivos simultaneamente)*/
            {
                await using var zipStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var zipInputStream = new ZipInputStream(zipStream);
                zipInputStream.IsStreamOwner = false;

                ZipEntry entry;
                while ((entry = zipInputStream.GetNextEntry()) != null)
                {
                    if (!entry.IsFile) continue;
                    Console.WriteLine($"Lendo: {entry.Name}");

                    int bytesRead;
                    while ((bytesRead = await zipInputStream.ReadAsync(buffer, 0, BufferSize)) > 0)
                    {
                        // Copia apenas os dados lidos em um novo buffer
                        byte[] chunk = ArrayPool<byte>.Shared.Rent(bytesRead);
                        chunk.AsSpan().Clear();
                        buffer.AsSpan(0, bytesRead).CopyTo(chunk);

                        // Envia para o canal
                        await writer.WriteAsync(chunk);
                    }
                    ArrayPool<byte>.Shared.Return(buffer);
                }
                writer.Complete();
            }

            // A segunda ideia seria tira a cabeça e o rabo (tail), e ir juntando eles em uma nova Chunk, quando alcançasse o tamanho do BufferSize, mandaria como uma nova Chunk
            // fazer um segundo teste com a primeira ideia
            /*async Task LineSplitter(ChannelReader<byte[]> reader, ChannelWriter<byte[]> writer)
            {
                using var brokenLineBuffer = new MemoryStream();
                using var connectedLinesBuffer = new MemoryStream(BufferSize);
                await foreach (var chunk in reader.ReadAllAsync())
                {
                    // Tive duas ideias para solucionar o problema (De alocação de memoria desnecessária), e tem mais a do GPT
                    // A primeira e ter dois bytes, um maior para levar a chunk toda, e uma para o Tail, e mandaria as duas juntas, que seria tratada pelo Processor
                    //Console.WriteLine(Encoding.Latin1.GetString(chunk));
                    int endFirstLineIndex = Array.IndexOf(chunk, (byte)'\n');
                    int endChunkIndex = Array.LastIndexOf(chunk, (byte)'\n');
                    int completeLength = endChunkIndex - endFirstLineIndex;

                    if (endFirstLineIndex == -1 || endChunkIndex == -1 || endChunkIndex <= endFirstLineIndex) {
                        Console.WriteLine("Chunk Line Error");
                        continue;
                    }
                    byte[] safeChunk = ArrayPool<byte>.Shared.Rent(completeLength);
                    //Solução Temporária
                    safeChunk.AsSpan().Clear();
                    //Console.WriteLine("Tamanho Completo: " + completeLength);
                    //Console.WriteLine("Safe Chunk Antes: " + safeChunk.Length + "\n");

                    chunk.AsSpan(endFirstLineIndex + 1, completeLength).CopyTo(safeChunk);
                    await writer.WriteAsync(safeChunk);

                    //Console.WriteLine("Chunk normal: " + Encoding.Latin1.GetString(chunk));
                    //Console.WriteLine("Safe Chunk: " + Encoding.Latin1.GetString(safeChunk) + "\n");

                    
                    //posso deixar write async para maior eficiencia, mas vou ter que modificar algumas coisas
                    int firstLineSize = endFirstLineIndex + 1;
                    int lastLineSize = chunk.Length - (endChunkIndex + 1);
                    brokenLineBuffer.Write(chunk, 0, firstLineSize);
                    if (BufferSize < (int)connectedLinesBuffer.Length + (int)brokenLineBuffer.Length)
                    {
                        var connectedLineChunk = ArrayPool<byte>.Shared.Rent((int)connectedLinesBuffer.Length);
                        connectedLineChunk.AsSpan().Clear();
                        connectedLinesBuffer.ToArray().AsSpan(0, (int)connectedLinesBuffer.Length).CopyTo(connectedLineChunk);
                        await writer.WriteAsync(connectedLineChunk);
                        //Console.WriteLine("Chunk BrokenLines: " + Encoding.Latin1.GetString(connectedLineChunk));
                        connectedLinesBuffer.Position = 0;
                        connectedLinesBuffer.SetLength(0);
                    }
                    if (brokenLineBuffer.ToArray()[^1] == (byte)'\n')
                    {
                        connectedLinesBuffer.Write(brokenLineBuffer.ToArray());
                    }
                    else
                    {
                        Console.WriteLine("Tem parada errada aqui");
                    }
                    brokenLineBuffer.Position = 0;
                    brokenLineBuffer.Write(chunk, endChunkIndex + 1, lastLineSize);
                    brokenLineBuffer.SetLength(lastLineSize);

                    ArrayPool<byte>.Shared.Return(chunk);
                }
                if(connectedLinesBuffer.Length > 0){
                    var connectedLineChunk = ArrayPool<byte>.Shared.Rent((int)connectedLinesBuffer.Length);
                    connectedLineChunk.AsSpan().Clear();
                    connectedLinesBuffer.ToArray().AsSpan(0, (int)connectedLinesBuffer.Length).CopyTo(connectedLineChunk);
                    await writer.WriteAsync(connectedLineChunk);
                    //Console.WriteLine("Chunk BrokenLines: " + Encoding.Latin1.GetString(connectedLineChunk));
                    connectedLinesBuffer.Position = 0;
                    connectedLinesBuffer.SetLength(0);
                }
            }*/

            async Task LineSplitter(ChannelReader<byte[]> reader, ChannelWriter<ReadOnlyMemory<byte>> writer)
            {
                using var tail = new MemoryStream();

                await foreach (var chunk in reader.ReadAllAsync())
                {
                    ReadOnlySpan<byte> span = chunk;

                    int newlinePos;
                    int offset = 0;

                    // Scan for newlines
                    while ((newlinePos = span.Slice(offset).IndexOf((byte)'\n')) >= 0)
                    {
                        int absolute = offset + newlinePos + 1;
                        int len = absolute;

                        if (tail.Length > 0)
                        {
                            // Combine tail + head to form full line
                            tail.Write(span.Slice(0, absolute - offset));
                            writer.TryWrite(tail.ToArray());
                            tail.SetLength(0);
                        }
                        else
                        {
                            // Full line is already inside this chunk
                            writer.TryWrite(chunk.AsMemory(0, absolute));
                        }

                        span = span.Slice(absolute);
                        offset = 0;
                    }

                    // tail: leftover (unfinished line)
                    if (span.Length > 0)
                        tail.Write(span);

                    ArrayPool<byte>.Shared.Return(chunk);
                }

                // flush leftover final line
                if (tail.Length > 0)
                    writer.TryWrite(tail.ToArray());
            }

            //async Task Processor(ChannelReader<byte[]> reader, ChannelWriter<BsonDocument> writer)
            /* [Processor]
            - Consumidor do Canal DataDownload e Produtor do Canal DataProcess
            - Irá realizar o processamento dos blocos fornecidos pelo Downloader
            - Será feita a descompactação (leitura do arquivo)
            - E criação do BsonDocument
            - E a subtituição das aspas para vazio
            - Provavel de ter mais de 1 para esse processo por arquivo*/
            /*{
                await foreach (var chunk in reader.ReadAllAsync())
                {
                    //Chunks.Add(chunk);
                    //Console.WriteLine(Encoding.Latin1.GetString(chunk));
                    // devolve pro pool

                    int start = 0;
                    for (int i = 0; i < chunk.Length; i++)
                    {
                        if (chunk[i] == (byte)'\n')
                        {
                            int length = i - start + 1;
                            string line = Latin1Encoding.GetString(chunk, start, length).Trim();
                            var parts = line.Split(';');

                            // Example transformation
                            var doc = new BsonDocument();
                            try
                            {
                                for (int j = 0; j < thisHeaderCollection.Length; j++)
                                {
                                    doc[thisHeaderCollection[j]] = parts[j].Trim('\"');
                                }
                                await writer.WriteAsync(doc);
                            } catch (Exception ex)
                            {
                                Console.WriteLine($"Line: {line}");
                                //Console.WriteLine("Chunk: " + Encoding.Latin1.GetString(chunk));
                                //Console.WriteLine(ex.Message);
                            }
                            start = i + 1;
                        }
                    }
                    ArrayPool<byte>.Shared.Return(chunk);
                }
            }*/

            async Task Processor(ChannelReader<ReadOnlyMemory<byte>> reader,
                     ChannelWriter<BsonDocument> writer)
            {
                await foreach (var mem in reader.ReadAllAsync())
                {
                    ReadOnlySpan<byte> line = mem.Span;

                    if (line.Length == 0)
                        continue;

                    BsonDocument doc = ParseCsvLine(line);
                    await writer.WriteAsync(doc);
                }

                writer.Complete();
            }


            BsonDocument ParseCsvLine(ReadOnlySpan<byte> line)
            {
                var doc = new BsonDocument();

                int start = 0;
                int headerIdx = 0;

                for (int i = 0; i <= line.Length; i++)
                {
                    if (i == line.Length || line[i] == (byte)';' || line[i] == (byte)'\n')
                    {
                        ReadOnlySpan<byte> fieldSpan = line.Slice(start, i - start);

                        string field = Latin1Encoding.GetString(fieldSpan);

                        doc[thisHeaderCollection[headerIdx++]] = field;
                        start = i + 1;
                    }
                }

                return doc;
            }

            async Task Importer(ChannelReader<BsonDocument> reader)
            /* [Importer]
            - Consumidor do Canal DataProcess
            - Irá realizar a importação dos Bson Document para o MongoDB
            - Provavel que terá mais de um para esse processo*/
            {
                List<BsonDocument> batch = new();

                await foreach (var doc in reader.ReadAllAsync())
                {
                    batch.Add(doc);
                    if (batch.Count >= batchSize)
                    {
                        //Console.WriteLine("Vou inserir");
                        await collection.InsertManyAsync(batch, opts);
                        batch.Clear();
                    }
                }

                if (batch.Count > 0)
                    await collection.InsertManyAsync(batch);
            }
        }

        private static async Task DropAllCollectionsAsync()
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

        // CheckConnection

        public static async Task<bool> CheckMongoConnection()
        {
            try
            {
                var settings = MongoClientSettings.FromConnectionString(ConnectionDatabaseConfig["ConnectionString"]);
                settings.ConnectTimeout = TimeSpan.FromSeconds(5);
                settings.SocketTimeout = TimeSpan.FromSeconds(5);
                settings.ServerSelectionTimeout = TimeSpan.FromSeconds(5);

                var databaseTeste = new MongoClient(settings).GetDatabase(ConnectionDatabaseConfig["DatabaseName"]);

                await databaseTeste.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1));
                Console.WriteLine("- Sucessfully MongoDB Conection");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"- Erro MongoDB: {ex.Message}");
                return false;
            }
        }

        public static async Task<bool> CheckHttpConnection()
        {
            try
            {
                var response = await httpClient.GetAsync(baseUrl);
                Console.WriteLine("- Sucessfully Http Conection");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"- Erro Http: {ex.Message}");
                return false;
            }
        }

        // Melhorar Retry

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
                    Console.WriteLine($"Falha na {attempt}° tentativa");
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
