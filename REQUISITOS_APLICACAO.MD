# DADOS RECEITA PARA MONGODB
### Ideias Gerais do Projeto
- **Retry inteligente:** caso haja falha na conexão com a Receita ou com o MongoDB, tentar até 3 vezes antes de agendar novo retry.
- **Monitoramento de Recursos:** Tomar conhecimento das informações do computador em que está sendo executado o programa, e executar com base nas configurações do computador ou de limites estabelecidos pela aplicação, utilizando da melhor maneira os recursos disponiveis (Threads, RAM e Disco). Também monitorar com base no desempenho do programa.
- **Persistência do progresso:** salvar checkpoints de cada arquivo, incluindo bloco/linha em que parou, para retomar downloads/parsing/import sem reprocessar tudo.
- o processo vai ser realizado pipeline, porém vou testar com o mongoimport posteriormente para ver qual tem melhor desempenho
- Para cada uma das etapas (Download, Tratamento e Import), eles irão utilizar o padrão Produtor-Consumidor, Download manda para Tratamento, e Tratamento manda para Import. Todo esse processo vai ser realizado em pipeline.
- Pegar a pasta mais recente disponivel, e ver se é daquele mês, se não for tentar novamente o outro dia, repetir até encontrar.

### 1° Download
- Download de multiplos arquivos
- Se o tratamento de dados for mais devagar que o download, em algum momento vai encher a RAM, nesse caso irei começar armazenar os dados no disco, depois que finalizar todos os dados executados em pipeline, eu leio do disco (armazenar informações dos arquivos e do bloco em que deu problema)
- Baixar o arquivo em partes (exemplo, irei separar o download desse arquivo em duas partes, vai consumir a metade do tempo para baixar) para maior velocidade de download e melhor aproveitamento de banda. E também dizer para o Tratamento, qual parte do download ele está responsavel (como vou dividir o em partes, eu preciso dizer quais linhas ele vai estar responsavel, para caso ocorra algum erro no processo, quando for reiniciar o download saber qual parte deve ser baixada para não precisar baixar tudo caso ocorra algum erro)
- Download de 3 arquivos por minuto
- Caso haja falha na tentativa de fazer o download de um arquivo especifico (também após os retry), salvar em algum lugar o arquivo que deu problema, e tentar novamente após algum tempo, enquanto isso vai baixando os outros arquivos que não deram problema

### 2° Tratamento
- Processar em Blocos(chunks/batch de 5000 linhas), e mandar para o import em blocos mesmo
- Caso em algum momento dê problema em algum bloco (seja no download ou em qualquer outra parte), cancela todo o processo daquele arquivo em especifico e começa de novo (inicia o download a partir do bloco que deu problema)


#### Tipos de Tratamento
1. **InsertManyAsync**
- Leitura Zip
- Retirada das Aspas
- Split por ;
- BsonDocument

2. **mongoimport**
- Descompactação
- Troca de ; para ,
- Conversão para utf-8

### 3° Import
- Realiza a inserção do batch no Banco de Dados
- Posteriormente realiza o descarte do batch para liberar a RAM