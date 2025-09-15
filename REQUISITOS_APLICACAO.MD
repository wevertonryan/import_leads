# DADOS RECEITA PARA MONGODB
### Ideias Gerais do Projeto
- **Retry inteligente:** Caso haja falha na conexão com a Receita ou com o MongoDB, tentar até 3 vezes antes de agendar novo retry.
- **Monitoramento de Recursos:** Tomar conhecimento das informações do computador em que está sendo executado o programa, e executar com base nas configurações do computador ou de limites estabelecidos pela aplicação, utilizando da melhor maneira os recursos disponiveis (Threads, RAM e Disco). Também monitorar com base no desempenho do programa, e ajustar caso o limite seja excedido (realizando o ajuste de produtores e consumidores).
- **Persistência do progresso:** Salvar checkpoints de cada arquivo, incluindo bloco/linha em que parou, para retomar downloads/parsing/import sem reprocessar tudo.
- **Pipeline Produtor-Consumidor:** Três etapas separadas (Download → Tratamento → Import) com canais/buffers, desacopladas para lidar com variação de velocidade entre etapas.
- **Dados Atualizados:** Pegar a pasta mais recente disponivel, e ver se é daquele mês, se não for tentar novamente o outro dia, repetir até encontrar.
- **Suporte a multi-arquivo:** Múltiplos arquivos da Receita baixados e processados em paralelo, respeitando limites de recursos.
- Testar posteriormente com o mongoimport para ver qual tem melhor desempenho.

## 1° Download
- **Controle de memória:** caso o pipeline encha a RAM, gravar temporariamente no disco (fila de overflow) para não travar a aplicação.
- **Download em partes:** dividir arquivos grandes em ranges ou chunks para aproveitar melhor a banda e permitir retomada parcial, fornecer informações para a próxima etapa quais linhas de download estará responsavel (Ex: linha 2000 a 5000).
- **Arquivos simultaneos:** Download de 3 arquivos por minuto (limite da receita federal).
- **Retry por chunk:** se um chunk falhar (ex: queda de rede), recomeçar só aquele bloco, evitando baixar tudo novamente.
- **Persistência do progresso:** salvar metadados do download (arquivo, chunk atual, tamanho baixado) em banco ou arquivo JSON.
- **Procedimento em caso de falha:** Caso haja falha na tentativa de fazer o download de um arquivo especifico (também após os retry), salvar em algum lugar o arquivo que deu problema e os dados de download até agora, e tentar novamente após algum tempo, enquanto isso vai baixando os outros arquivos que não deram problema.
- **Logs detalhados:** registrar cada tentativa de download, falha e retomada, incluindo timestamp e informações do ambiente. (Acho que não vou adicionar)

## 2° Tratamento
- **Processamento em blocos:** usar batches de 5000 linhas (ou ajustável conforme memória) para transformação e envio ao MongoDB.
- **Logs e auditoria:** registrar cada bloco processado, tempo de processamento e eventuais warnings.
- **Monitoramento de performance:** medir tempo por batch e ajustar número de consumidores dinamicamente se possível.

### Tipos de Tratamento
1. **InsertManyAsync**
- Leitura Zip
- Retirada das Aspas
- Split por ;
- BsonDocument

2. **mongoimport**
- Descompactação
- Troca de ; para ,
- Conversão para utf-8

## 3° Import
- **Inserção em batches:** enviar blocos ao MongoDB (InsertManyAsync) ou chamar mongoimport por arquivo/batch.
- **Descarte de memória:** após inserção, limpar referências e liberar RAM.
- **Validação pós-inserção:** opcionalmente, verificar quantidade de documentos inseridos vs. quantidade do batch, para garantir integridade.
- **Paralelismo controlado:** múltiplos consumidores inserindo batches, respeitando limites de conexões e RAM.
