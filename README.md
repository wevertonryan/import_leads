# Importação de Dados de CNPJ da Receita Federal para MongoDB

<br>

## 🚀 Sobre o Projeto

`import_leads` é uma ferramenta de linha de comando em C\# projetada para automatizar o processo de download, extração e importação dos dados abertos de CNPJs da Receita Federal diretamente para um banco de dados MongoDB.

Este projeto busca a máxima **performance** e **eficiência** no processamento de grandes volumes de dados, utilizando:

  - **Multithreading e Paralelismo:** Para gerenciar downloads e importações simultâneas.
  - **Processamento em Stream:** Para corrigir e converter dados sem sobrecarregar a memória do sistema.

O projeto está em desenvolvimento contínuo, focado em otimizações de performance e na futura implementação de recursos de segurança e resiliência. Embora desenvolvido para atender a um projeto maior, sinta-se à vontade para utilizá-lo e contribuir.

<br>

-----

## 🛠️ Tecnologias e Ferramentas

  - **Linguagem:** C\# (.NET 8.0)
  - **Downloads:** `HttpClient` nativo do .NET
  - **Extração de ZIP:** `ICSharpCode.SharpZipLib`
  - **Controle de Paralelismo:** `SemaphoreSlim`
  - **Importação:** `mongoimport` (ferramenta de linha de comando do MongoDB)
  - **Banco de Dados:** MongoDB

<br>

-----

## ⚙️ Requisitos para Execução

Antes de rodar o projeto, certifique-se de que as seguintes dependências estão instaladas no seu ambiente:

1.  **.NET 8.0 SDK:** Para compilar e executar o projeto.
2.  **MongoDB:** O servidor do MongoDB deve estar em execução.
3.  **MongoDB Database Tools:** O `mongoimport.exe` é essencial para a importação dos dados. Certifique-se de que ele está instalado e o caminho para o executável está configurado na variável de ambiente **`PATH`** do seu sistema. <br>

OBS: No arquivo `MongoImport.cs` que está no projeto ao ser Instalado, na função `ImportarArquivoAsync()` terá uma variavel `psi` que irá conter um `FileName:` que irá conter uma `string` com o caminho para o executal do `mongoimport`, altere com base no caminho que estiver no seu computador.

<br>

-----

## 📝 Como Usar

### Instalação

1.  Clone este repositório para a sua máquina local:
    ```bash
    git clone https://github.com/wevertonryan/import_leads.git
    ```
2.  Abra a solução (`.sln`) no **Visual Studio** ou use o seu editor de código preferido.

### Pacotes NuGet

O projeto utiliza os seguintes pacotes NuGet. Ao abrir a solução é para eles serem baixados automaticamente, mas recomendo verificar na pasta `pacotes`, que está dentro de `dependencias`, caso não estejam você terá que instalar manualmente via Gerenciador de Pacote:

  - `ICSharpCode.SharpZipLib`
  - `MongoDB.Driver`

### Execução

Após restaurar os pacotes, você pode executar o projeto diretamente do Visual Studio ou via linha de comando:

```bash
dotnet run
```

O programa irá automaticamente iniciar o processo de download dos arquivos da Receita Federal, extraí-los, corrigir o delimitador e importar os dados para o MongoDB. O progresso será exibido no console.

<br>

-----

## 🤝 Contribuição

Contribuições são bem-vindas\! Se você tiver alguma ideia para melhorar a performance, adicionar novas funcionalidades ou corrigir bugs, sinta-se à vontade para abrir uma *issue* ou enviar um *pull request*.

<br>

-----

## 🛡️ Segurança e Futuro

Atualmente, o projeto foca em desempenho e funcionalidade. A segurança, como a criptografia de dados em trânsito e o acesso seguro ao banco de dados, é uma prioridade para futuras versões. As otimizações de performance também continuarão sendo um ponto central do desenvolvimento.
