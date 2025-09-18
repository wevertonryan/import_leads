## Progresso Geral da Importação
### 1. Informações
* **Status:** Configurando | Em Progresso | Interrompido (mensagem de erro) | Completo
* **DataHora de Início:** timestamp
* **DataHora de Conclusão:** timestamp (se aplicável)
* **Tempo Decorrido:** `TimeSpan` acumulado apenas enquanto a importação estava ativa
* **Armazenamento Necessário (KB):** Armazenamento necessário para conseguir baixar todos os dados no dispositivo
* **Total Importado (KB):** Total importado em KB até o momento
### 2. Tabelas MongoDB
  ```text
    Collection1:
          - Storage Size: 1.5GB
          - Documents: 5.2M
  ```
### 3. Documentos
  ```text
    DocumentoA: Completo (2763/2763 registros importados)
    DocumentoB1: Em Progresso (15452/47890 registros importados)
    DocumentoB2: Corrompido (???/??? registros importados)
  ```
* **Total de Documentos:** int
* **Documentos Concluídos:** int
* **Documentos em Progresso:** int
* **Documentos Corrompidos/Erro:** int
### 4. Logs (Não sei exatamente como fazer, vou criar algo da minha cabeça)
* **Nível:** INFO | WARNING | ERROR
* **TimeStamp**
* **Mensagem Erro**
* **Referencias:** Linhas que deu erro, Documentos Corrompidos, etc...

<br>

## Progresso por Documento:
* **Nome do Documento**
* **Status:** Em Progresso | Interrompido (mensagem de erro) | Corrompido | Completo
* **Peso Total (KB)**
* **Total de Registros:** int
* **Registros Importados com Sucesso:** int ou blocos
* **Registros Faltantes:** int
* **Registros Corrompidos:** linhas específicas (array/lista)
* **Tentativas de Download:** int (opcional, útil para retry tracking)
* **Logs:** mensagens específicas para aquele documento

## Exemplo
```json
{
    "status": "Em Progresso",
    "timeStampInicio": "2025-09-18T12:32:02Z",
    "timeStampConclusao": "2025-09-18T13:26:15Z",
    "tempoDecorrido": "00:54:13",
    "armazenamentoNecessario": 15000000,
    "totalImportado": 10000000,
    "tabelasMongoDB": {
        "Collection1": {
            "storageSize": 5000000,
            "documents": 7200000
        },
        "Collection2": {
            "storageSize": 6000000,
            "documents": 8205000
        }
    },
    "documents": {
        "DocumentoA": {
            "status": "Em Progresso",
            "peso": 1500000,
            "totalRegistros": 5120000,
            "registrosImportados": [
                {"inicio": 0, "fim": 1280000, "atual": 1280000},
                {"inicio": 1280001, "fim": 2560000, "atual": 2069213},
                {"inicio": 2560000, "fim": 3840000, "atual": 3250125},
                {"inicio": 3840001, "fim": 5120000, "atual": 5120000}
            ],
            "registrosFaltantes": 1961644,
            "registrosCorrompidos": [5, 7, 14, 56, 123, 46746, 237774],
            "tentativasDownload": 1,
            "logs": {}
        }
    },
    "totalDocumentos": 15,
    "documentosConcluidos": 2,
    "documentosProgresso": 5,
    "documentosCorrompidos": 1,
    "logs": {}
}
```