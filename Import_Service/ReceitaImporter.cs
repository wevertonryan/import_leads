using ICSharpCode.SharpZipLib.Zip;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Configuration;
using SharpCompress.Common;
using SharpCompress.Writers;
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
        private static readonly string[] filesArray = ["Cnaes", "Empresas0", "Empresas1", "Empresas2", "Empresas3", "Empresas4","Empresas5", "Empresas6", "Empresas7","Empresas8", "Empresas9","Estabelecimentos0","Estabelecimentos1","Estabelecimentos2","Estabelecimentos3","Estabelecimentos4","Estabelecimentos5","Estabelecimentos6","Estabelecimentos7","Estabelecimentos8","Estabelecimentos9", "Motivos", "Municipios","Naturezas","Paises", "Qualificacoes","Simples","Socios0","Socios1","Socios2","Socios3","Socios4","Socios5","Socios6","Socios7", "Socios8", "Socios9"];
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
            List<string> ErrorLines = new List<string>();
            var file = baseUrl + fileName + ".zip";
            var channelOptions = new BoundedChannelOptions(50000) { FullMode = BoundedChannelFullMode.Wait }; //quando encher, começar a escrever no disco para não atrapalhar o download, ou alguma outra etapa
            fileName = Regex.Replace(fileName, @"[0-9]", "");
            var thisHeaderCollection = headers[fileName];

            var DataDownload = Channel.CreateBounded<byte[]>(channelOptions);
            var DataProcess = Channel.CreateBounded<BsonDocument>(channelOptions);

            ICollection<Task> downloaders = new List<Task>();
            ICollection<Task> processors = new List<Task>();
            ICollection<Task> importers = new List<Task>();
            List<byte[]> Chunks = new();
            const int batchSize = 5000;

            const int BufferSize = 8 * 1024; // 8KB por leitura

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
                downloaders.Add(Downloader(DataDownload.Writer));
            }
            for (int i = 0; i < 3; i++)
            {
                processors.Add(Processor(DataDownload.Reader, DataProcess.Writer));
            }

            var opts = new InsertManyOptions { IsOrdered = false };
            var collection = mongoDatabase.GetCollection<BsonDocument>(fileName);
            for (int i = 0; i < 1; i++)
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

            if (ErrorLines.Count() > 0)
            {
                var path = AppDomain.CurrentDomain.BaseDirectory + @"ReiceitaImporterLog\";
                Directory.CreateDirectory(path);
                path += $"ErrorLines{fileName}.txt";
                foreach (string content in ErrorLines)
                {
                    File.AppendAllText(path, $"{content}\n");
                }
                Console.WriteLine($"Arquivo ErrorLines{fileName}.txt criado/atualizado");
            }

            async Task Downloader(ChannelWriter<byte[]> writer)
            /* [Downloader]
            - Produtor do Canal DataDownload
            - Irá realizar o download dos arquivos armazenando na RAM em Stream
            - Irá extrair
            - Ele irá armazenar em blocos (bytes), e irá adicionar esse blocos no Canal DataDownload
            - Terá provavelmente apenas 1, para baixar o arquivo inteiro, ou mais para realizar o download em partes 
            (só terá mais se tiver mais recurso disponivel mesmo baixando 3 arquivos simultaneamente)*/
            {
                byte[] buffer = ArrayPool<byte>.Shared.Rent(BufferSize);

                await using var zipStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var zipInputStream = new ZipInputStream(zipStream);
                zipInputStream.IsStreamOwner = false;

                ZipEntry entry;
                while ((entry = zipInputStream.GetNextEntry()) != null)
                {
                    if (!entry.IsFile) continue;
                    Console.WriteLine($"Lendo: {entry.Name}");

                    int bytesRead;
                    int totalChunk;
                    int remaning = 0;

                    // utilizar o PipeReader invés do ReadAsync (pesquisar mais sobre)
                    while ((bytesRead = await zipInputStream.ReadAsync(buffer, remaning, BufferSize - remaning)) > 0)
                    {
                        buffer.AsSpan(remaning + bytesRead).Clear(); //tem que ver se está certo
                        totalChunk = buffer.AsSpan().LastIndexOf((byte)'\n') + 1; //quantidade, mas tbm significa o inicio da ultima linha
                        // Copia apenas os dados lidos em um novo buffer
                        byte[] chunk = ArrayPool<byte>.Shared.Rent(totalChunk); //pega um array com a quantidade do totalChunk
                        chunk.AsSpan(totalChunk).Clear(); //limpa o restante que veio do array pool
                        
                        buffer.AsSpan(0, totalChunk).CopyTo(chunk); //transforma o buffer em uma view do 0 até o ultimo \n, já que aqui é o count, e não indice, e manda para chunk
                        // Envia para o canal
                        await writer.WriteAsync(chunk);
                        remaning = buffer.Length - totalChunk;
                        buffer.AsSpan(totalChunk, remaning).CopyTo(buffer);
                        buffer.AsSpan(remaning + 1).Clear();
                    }
                    ArrayPool<byte>.Shared.Return(buffer);
                }
            }

            async Task Processor(ChannelReader<byte[]> reader,
                     ChannelWriter<BsonDocument> writer)
            {
                int fullLine;
                await foreach (var chunk in reader.ReadAllAsync())
                {
                    ReadOnlyMemory<byte> chunkMemory = chunk;
                    while ((fullLine = chunkMemory.Span.IndexOf((byte)'\n')) >= 0) {
                        fullLine++;
                        ReadOnlyMemory<byte> line = chunkMemory.Slice(0, fullLine);
                        chunkMemory = chunkMemory.Slice(fullLine);

                        if (chunkMemory.IsEmpty) continue;

                        try {
                            BsonDocument doc = ParseCsvLine(line.Span);
                            await writer.WriteAsync(doc);
                        } catch(Exception ex)
                        {
                            ErrorLines.Add(ex.Message);
                        }
                    }
                    ArrayPool<byte>.Shared.Return(chunk);
                }
            }
            BsonDocument ParseCsvLine(ReadOnlySpan<byte> line)
            {
                var doc = new BsonDocument();

                int start = 0;
                int headerIdx = 0;

                try
                {
                    for (int i = 0; i < line.Length; i++)
                    {
                        if (line[i] == (byte)';' && line[i - 1] == (byte)'\"' && line[i + 1] == (byte)'\"')
                        {
                            addFieldToDocument(line, start, i, headerIdx);
                            start = i + 1;
                            headerIdx++;
                        }
                    }
                    addFieldToDocument(line, start, line.Length - 1, headerIdx);
                }
                catch (Exception)
                {
                    throw new Exception(Latin1Encoding.GetString(line));
                }
                return doc;
                void addFieldToDocument(ReadOnlySpan<byte> line, int start, int end, int headerIdx)
                {
                    ReadOnlySpan<byte> fieldSpan = line.Slice(start + 1, (end - start) - 2);
                    doc[thisHeaderCollection[headerIdx]] = Latin1Encoding.GetString(fieldSpan);
                }
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
