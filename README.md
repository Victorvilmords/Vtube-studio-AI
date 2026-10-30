# VTuber Studio AI

Base desktop inicial para organizar e preparar personagens virtuais, com foco em personagens furry e antropomórficos. O desenvolvimento é local-first: esta fundação não envia arquivos ou dados a serviços externos.

## Tecnologias

- C# e .NET 10 LTS
- WPF e XAML
- MVVM leve com `INotifyPropertyChanged` e `ICommand` do .NET
- xUnit para testes automatizados
- Solução no formato `.slnx`, gerada pelo SDK instalado

## Requisitos

- Windows 10 ou superior
- .NET 10 SDK
- Runtime Windows Desktop do .NET 10

## Compilar e executar

Na raiz do repositório:

```powershell
dotnet restore .\VTuberStudioAI.slnx
dotnet build .\VTuberStudioAI.slnx
dotnet run --project .\src\VTuberStudioAI.App\VTuberStudioAI.App.csproj
```

Também é possível iniciar o executável já compilado em `publish\win-x64\VTuberStudioAI.App.exe`. Mantenha os arquivos `.dll`, `.deps.json` e `.runtimeconfig.json` ao lado do `.exe`. Esta cópia usa o runtime .NET 10 Desktop instalado no Windows; ela não é uma publicação autocontida.

A versão autocontida para Windows x64, em um único arquivo e sem exigir a instalação do .NET, é gerada em `publish\win-x64-standalone\VTuberStudioAI.App.exe` com:

```powershell
dotnet publish .\src\VTuberStudioAI.App\VTuberStudioAI.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None --output .\publish\win-x64-standalone
```

Se a publicação falhar com `UnauthorizedAccessException` em `VTuberStudioAI.App.exe`, feche qualquer instância do aplicativo que esteja usando essa pasta e tente novamente. Para publicar sem substituir a cópia existente, use outra pasta no argumento `--output`.

## Testes

Execute os testes xUnit da raiz da solução:

```powershell
dotnet test .\VTuberStudioAI.slnx
```

Os testes verificam as oito rotas de navegação, as operações da biblioteca, a persistência JSON, a validação e decodificação de PNG/JPEG, cancelamento de prévia, limites de imagem, persistência de recursos e configurações furry.

## Biblioteca de projetos

Os projetos ficam em `%LOCALAPPDATA%\VTuberStudioAI\Projects\<id>\project.json`. Cada arquivo inclui identificador, nome, versão do formato, datas de criação/modificação, tipo de conteúdo, referências relativas a recursos e configurações JSON. O formato atual é a versão `1`. Configurações do Furry Studio são salvas em `configurations["furry.components"]`, sem alterar a versão do documento.

Os metadados são validados ao listar ou abrir projetos. Um JSON corrompido, uma versão incompatível ou um recurso referenciado ausente gera uma mensagem de erro em vez de ser ignorado ou sobrescrito. As gravações usam um arquivo temporário na pasta do projeto e substituição controlada. A duplicação copia recursos referenciados para a nova pasta do projeto.

## Importação de imagens

O fluxo Criar VTuber aceita PNG e JPEG/JPG decodificados pelo WIC/WPF do Windows. WebP não é aceito porque o projeto não inclui um decodificador WebP. O limite é 25 MiB, 12.000 pixels por lado e 40 megapixels. A imagem é validada e pré-visualizada antes da confirmação; após confirmar, uma cópia com nome controlado é gravada em `resources` dentro do projeto e uma referência relativa é persistida. O original nunca é alterado. Remover a referência não apaga a cópia nem o original.

Zoom tem limites entre 10% e 500%; Ajustar e Restaurar retornam a visualização ao enquadramento ajustado.

## Fluxo para começar

Há um único lugar para iniciar um personagem: **Criar VTuber**. Home, Biblioteca e Furry Studio levam a esse mesmo fluxo, sem formulários repetidos. “Começar do zero” cria um projeto vazio e abre o Furry Studio; não converte nem altera um personagem existente. Para usar uma arte que já existe, escolha um projeto salvo e importe a imagem. As alterações do projeto são persistidas localmente pelas operações correspondentes.

## Furry Studio

O Furry Studio permite configurar orelhas, chifres, focinho, cauda, pelagem/tufos, acessórios e componentes personalizados. Cada tipo expõe somente seus próprios parâmetros, com valores padrão e limites validados. As configurações e os estados ativo/inativo ficam no projeto local e são validados ao carregar ou salvar. Há configurações iniciais de exemplo identificadas como configurações, não animações. Nada nessa tela aplica deformação, rigging, física ou animação à imagem.

## Preview interativo (aba Preview)

A aba **Preview** (logo abaixo de Furry Studio) pede para escolher o VTuber (projeto com imagem). No preview: **um clique num olho marcado** faz piscar; **dois cliques** (ou o botão “Seguir o mouse”) ligam/desligam o modo em que o quadro da imagem (cabeça: leve deslocamento e inclinação) e o conteúdo da área dos olhos acompanham o mouse. Use **Marcar olhos** e arraste sobre cada olho (até 4); as áreas são salvas no projeto (`preview.eyes`, coordenadas relativas à imagem). A pálpebra é uma sobreposição com a cor da pele ao redor (~0,24 s). É uma aproximação: não há rigging, camadas animadas nem deformação real, e a imagem original não é alterada. O olho fechado por camada PSD ainda não é usado.

## Importação de PSD com camadas

Além de PNG/JPEG, é possível importar **PSD** (RGB, 8 bits por canal, até 500 MiB e 100 camadas, até 12.000 × 12.000 px) pelo mesmo seletor em Criar VTuber. Imagens acima de 40 MP são exibidas em resolução reduzida (4096 px) para poupar memória; os arquivos do projeto mantêm a resolução total. O leitor é próprio (sem dependências) e valida cabeçalho, tamanhos, limites e compressão (raw, RLE e ZIP); arquivos corrompidos, PSB, CMYK/16 bits ou sem camadas são recusados com mensagem. Ao confirmar:

- as camadas viram PNGs separados em `resources/layers/` do projeto, listadas em `psd.layers`;
- uma composição das camadas visíveis (opacidade respeitada) vira a imagem principal;
- camadas visíveis chamadas “olho”/“eye” (exceto “fechado”, “closed”, “sobrancelha” etc.) viram áreas de olho no preview; ajuste com **Marcar olhos** se necessário.

Limitações: modos de mesclagem, máscaras, camadas de ajuste/efeitos e recorte são ignorados (tudo tratado como “normal”), grupos não são preservados e o PSD original não é copiado nem alterado. O olho fechado por camada ainda não é usado no piscar.

## Validação desta etapa

- `dotnet test .\VTuberStudioAI.slnx --configuration Debug`: compilação concluída; 54 aprovados, 0 falhos.
- Testes confirmam que “Começar do zero” cria um projeto vazio novo, abre o Furry Studio e não altera um projeto preexistente.
- Testes furry cobrem tipos e defaults, criação, renomeação, parâmetros, ativação/desativação, remoção, limites inválidos, presets, JSON corrompido, reabertura e preservação do arquivo de imagem.
- Automação de interface feita na etapa de importação confirmou avisos e limites exibidos e abertura/cancelamento do seletor nativo sem alterar projetos.
- Remover um componente agora exige confirmação; cancelar mantém os dados. Testes cobrem cancelamento e confirmação.
- Auditoria estática desta etapa não encontrou chaves de API, execução de JSON ou envio externo de imagens. A remoção de componente sem confirmação identificada foi corrigida.
- Verificação manual pendente: confirmar visualmente o diálogo de remoção no Windows e testar fechar/reabrir o aplicativo com um projeto furry real. Os testes automatizados verificam persistência fechando e recarregando o repositório, não reiniciando o processo WPF.
- Verificação manual de UX pendente: revisar Home, Biblioteca, Criar VTuber e Furry Studio em uma janela Windows real para conferir tamanhos, foco, lista suspensa escura e legibilidade em diferentes dimensões.

## Implementado nesta etapa

- Aplicativo WPF com tema escuro e navegação entre oito áreas.
- Tema de controles com campos, listas, seleções, hover e foco coerentes com a paleta escura e ciano.
- Revisão visual para reduzir títulos e mensagens repetidos, agrupar melhor o início de personagens e exibir controles de imagem somente quando podem ser usados.
- Combos e caixas de seleção com contraste e foco ciano, sem áreas claras do tema padrão do Windows.
- Barra lateral com ícones alinhados, rótulos adaptáveis, seleção sincronizada, hover e foco de teclado visíveis.
- Home com boas-vindas, projetos recentes persistidos, começo do zero e abertura da biblioteca.
- Biblioteca de projetos local com abertura, renomeação, duplicação, salvamento, confirmação de exclusão e persistência JSON versionada; a criação usa o fluxo único Criar VTuber.
- Importação local de imagens PNG/JPEG com validação, prévia cancelável, cópia para o projeto e controles de zoom.
- Furry Studio para configurar, validar e persistir componentes furry e parâmetros específicos por tipo.
- Páginas de roadmap identificadas como não implementadas.
- Área de estúdio com estado vazio explícito.
- Tratamento de exceções globais e registro local de erros.

Os logs são gravados em `%LOCALAPPDATA%\VTuberStudioAI\logs`.

## Planejado

Movimento, rigging, renderização e preferências do aplicativo.

## Limitações conhecidas

- A biblioteca grava metadados JSON em `%LOCALAPPDATA%\VTuberStudioAI\Projects`, um diretório por identificador.
- WebP não é suportado; as configurações furry são metadados e não deformam ou animam imagens; não há câmera, rastreamento, animação, renderização 3D ou integração com OBS/Live2D. A interface do diálogo de confirmação e o ciclo completo de reinicialização do app ainda aguardam teste manual.

## Estrutura

```text
VTuberStudioAI.slnx
src/
  VTuberStudioAI.App/
    App.xaml
    MainWindow.xaml
    Furry/
    Images/
    Projects/
    ViewModels/
tests/
  VTuberStudioAI.App.Tests/
    ProjectRepositoryTests.cs
    ImageImportTests.cs
    FurryStudioTests.cs
README.md
```
# Vtube-studio-AI
