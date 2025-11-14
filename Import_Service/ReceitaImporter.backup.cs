/*
using ICSharpCode.SharpZipLib.Zip;
using MongoDB.Bson;
using MongoDB.Driver;
using System.Buffers;
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;

namespace Import_Service
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

        private static async Task<string[]> GetFilesName(string baseUrl)
        {
            string html;
            html = await RetryAsync(() => httpClient.GetStringAsync(baseUrl), 3);
            return Regex.Matches(html, @"href=""([^""]+\.zip)""")
                    .Cast<Match>()
                    .Select(m => m.Groups[1].Value)
                    .ToArray();
        }

        public static async Task Start()
        {
            await DropAllCollectionsAsync();

            string baseUrl = "https://arquivos.receitafederal.gov.br/dados/cnpj/dados_abertos_cnpj/2025-08/";
            string[] zipMatches;
            try {
                zipMatches = await GetFilesName(baseUrl);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Falha ao obter a lista de arquivos da URL base: {baseUrl}");
                Console.WriteLine($"Erro: {ex.Message}");
                return;
            }
            var tasks = new List<Task>();

            Console.WriteLine("|=====|  INICIANDO DOWNLOAD DOS DADOS |=====|");
            var sw = new Stopwatch();
            sw.Start();

            foreach (string fileName in zipMatches)
            {
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

            string Tempo = String.Format("{0:00}h {1:00}m {2:00}s {3:000}ms", sw.Elapsed.Hours, sw.Elapsed.Minutes, sw.Elapsed.Seconds, sw.Elapsed.Milliseconds);
            Console.WriteLine($"Todos os downloads concluídos em {Tempo}.");
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




código feito por IA, tá ruim mas tem alguns conceitos interressantes
async Task BrokenLineRepairer(ChannelReader<byte[]> reader, ChannelWriter<byte[]> writer)
{
    const int MaxBufferSize = 1024 * 1024; // 1MB
    const int MaxLinesPerChunk = 1000; // Limite para enviar juntos

    byte[] buffer = ArrayPool<byte>.Shared.Rent(MaxBufferSize);
    int bufferIndex = 0;
    int linesInBuffer = 0;

    await foreach (var chunk in reader.ReadAllAsync())
    {
        try
        {
            int start = 0;
            for (int i = 0; i < chunk.Length; i++)
            {
                if (chunk[i] == (byte)'\n')
                {
                    int lineLength = i - start + 1;
                    if (bufferIndex + lineLength > buffer.Length)
                    {
                        Console.WriteLine("Linha muito longa, descartando.");
                        bufferIndex = 0;
                        linesInBuffer = 0;
                        start = i + 1;
                        continue;
                    }

                    chunk.AsSpan(start, lineLength).CopyTo(buffer.AsSpan(bufferIndex));
                    bufferIndex += lineLength;
                    linesInBuffer++;

                    // Se atingiu o limite de linhas, envia o pedaço
                    if (linesInBuffer >= MaxLinesPerChunk)
                    {
                        var partialChunk = new byte[bufferIndex];
                        buffer.AsSpan(0, bufferIndex).CopyTo(partialChunk);
                        await writer.WriteAsync(partialChunk);

                        bufferIndex = 0;
                        linesInBuffer = 0;
                    }

                    start = i + 1;
                }
            }

            // Copia o restante do chunk
            int remainingLength = chunk.Length - start;
            if (remainingLength > 0)
            {
                if (bufferIndex + remainingLength > buffer.Length)
                {
                    Console.WriteLine("Buffer cheio, descartando.");
                    bufferIndex = 0;
                    linesInBuffer = 0;
                }
                else
                {
                    chunk.AsSpan(start, remainingLength).CopyTo(buffer.AsSpan(bufferIndex));
                    bufferIndex += remainingLength;
                }
            }
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    // Envia o que sobrou no buffer (última parte incompleta ou completa)
    if (bufferIndex > 0)
    {
        var finalChunk = new byte[bufferIndex];
        buffer.AsSpan(0, bufferIndex).CopyTo(finalChunk);
        await writer.WriteAsync(finalChunk);
    }

    writer.Complete();
    ArrayPool<byte>.Shared.Return(buffer);
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

/*async Task LineSplitter(ChannelReader<byte[]> reader, ChannelWriter<ReadOnlyMemory<byte>> writer)
{
    using var tail = new MemoryStream();

    await foreach (var chunk in reader.ReadAllAsync())
    {
        ReadOnlySpan<byte> span = chunk;

        int newlinePos;

        // Scan for newlines
        while ((newlinePos = span.IndexOf((byte)'\n')) >= 0)
        {
            int absolute = newlinePos + 1;

            if (tail.Length > 0)
            {
                // Combine tail + head to form full line
                tail.Write(span.Slice(0, absolute));
                writer.TryWrite(tail.ToArray());
                tail.SetLength(0);
            }
            else
            {
                // Full line is already inside this chunk
                writer.TryWrite(chunk.AsMemory(0, absolute));
            }

            span = span.Slice(absolute);
        }

        // tail: leftover (unfinished line)
        if (span.Length > 0)
            tail.Write(span);

        ArrayPool<byte>.Shared.Return(chunk);
    }

    // flush leftover final line
    if (tail.Length > 0)
        writer.TryWrite(tail.ToArray());
}*/

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