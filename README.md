# VFtube AI

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

Também é possível iniciar o executável já compilado em `publish\win-x64\VFtubeAI.exe`. Mantenha os arquivos `.dll`, `.deps.json` e `.runtimeconfig.json` ao lado do `.exe`. Esta cópia usa o runtime .NET 10 Desktop instalado no Windows; ela não é uma publicação autocontida.

A versão autocontida para Windows x64, em um único arquivo e sem exigir a instalação do .NET, é gerada em `publish\win-x64-standalone\VFtubeAI.exe` com:

```powershell
dotnet publish .\src\VTuberStudioAI.App\VTuberStudioAI.App.csproj --configuration Release --runtime win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None --output .\publish\win-x64-standalone
```

Se a publicação falhar com `UnauthorizedAccessException` em `VFtubeAI.exe`, feche qualquer instância do aplicativo que esteja usando essa pasta e tente novamente. Para publicar sem substituir a cópia existente, use outra pasta no argumento `--output`.

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

O Furry Studio também **detecta o personagem pelas camadas do PSD**: o prefixo `F` (ex.: `F.ear.L`, `F_snout`) ou uma camada de focinho marca o personagem como furry; sem isso ele é tratado como humano. A lista "Partes detectadas" mostra cada camada reconhecida e o que o rig automático faz com ela (deforma ao virar a cabeça, pisca, abre a boca etc.). A lista é só leitura: renomeie camadas e ajuste a deformação na aba Preview. Os componentes manuais abaixo dela são configurações salvas no projeto.

O Furry Studio permite configurar orelhas, chifres, focinho, cauda, pelagem/tufos, acessórios e componentes personalizados. Cada tipo expõe somente seus próprios parâmetros, com valores padrão e limites validados. As configurações e os estados ativo/inativo ficam no projeto local e são validados ao carregar ou salvar. Há configurações iniciais de exemplo identificadas como configurações, não animações. Nada nessa tela aplica deformação, rigging, física ou animação à imagem.

## Preview interativo (aba Preview)

A aba **Preview** (logo abaixo de Furry Studio) pede para escolher o VTuber (projeto com imagem). No preview: **um clique num olho marcado** faz piscar; **dois cliques** (ou o botão “Seguir o mouse”) ligam/desligam o modo em que o quadro da imagem (cabeça: leve deslocamento e inclinação) e o conteúdo da área dos olhos acompanham o mouse. Use **Marcar olhos** e arraste sobre cada olho (até 4); as áreas são salvas no projeto (`preview.eyes`, coordenadas relativas à imagem). A pálpebra é uma sobreposição com a cor da pele ao redor (~0,24 s). É uma aproximação: não há rigging, camadas animadas nem deformação real, e a imagem original não é alterada. O olho fechado por camada PSD ainda não é usado.

## Nomenclatura de camadas e rig automático

Para PSDs importados, o preview lê o **nome de cada camada** no molde `[F.]parte[.L|.R]` (separadores: `.`, `_`, `-` ou espaço; maiúsculas/minúsculas e acentos não importam; `left/right/esquerda/direita` também valem como lado):

- **Partes:** `eyewhite` (fundo do olho), `iris`, `pupil`, `eyehighlight` (também `Eye_highlight`), `eyelash_upper`/`eyelash_lower` (cílios), `eyelid`, `eyebrow`/`brow`, `head`, `neck`, `ear`, `horn`, `snout`, `nose`, `hair_back`, `hair_side`, `hair_bangs`, `mouth`, `body`, `tail`, `background`/“Plano de fundo” (há apelidos em português, como `olho`, `orelha`, `cauda`). Camadas padrão como “Camada de pintura 2” não têm papel e acompanham a cabeça.
- **Lado:** `.L`/`_L` esquerda, `.R`/`_R` direita. Sufixos de duplicação (`copy`, `001`) são ignorados.
- **Prefixo `F`:** parte de personagem furry, por exemplo `F.ear.L` ou `F_ear_L`.

Com pelo menos uma camada reconhecida, o preview passa a desenhar as camadas separadas: o `eyewhite` do mesmo lado serve de **máscara** (íris, pupila e brilho nunca saem de dentro do olho) e eles se movem com o olhar; os olhos achatam ao piscar e os cílios superiores/inferiores acompanham a pálpebra do olho do mesmo lado; `body`/`neck`/`tail`/fundo ficam parados, orelhas (`ear`) inclinam levemente e as demais camadas acompanham a cabeça. Camadas `eyewhite` também definem os olhos clicáveis (a marcação manual fica desativada nesse caso). Sem nenhum nome reconhecido, o preview continua usando a imagem composta e a marcação manual.

Na aba Preview, a lista de camadas permite **renomear em tempo real**: o papel reconhecido aparece ao lado e o preview atualiza a cada letra; o nome é salvo no projeto (`psd.layers`) a cada alteração válida (1 a 64 caracteres, sem caracteres de controle). Os PNGs das camadas não são alterados.

Limitações: é um rig por camadas rígidas (deslocamento, inclinação, máscara e achatamento), sem malha, deformação, física nem IA. A nomenclatura apenas declara o papel; o prefixo F não é detecção automática de espécie. Camadas visíveis são decodificadas em até 2048 px e o preview em camadas é desativado (usa a composição) se o total passar de ~150 milhões de pixels. A opacidade das camadas só é respeitada em PSDs importados após esta versão. O olho fechado por camada (`eyelid`/“fechado”) ainda não é usado.

## Deformação pseudo-3D e editor de calibração

Quando o olhar vai para um lado (mouse ou giro de teste), cada camada com papel reconhecido recebe uma pose: nariz, focinho, boca, olhos e sobrancelhas deslocam mais que a cabeça; o lado que se afasta é achatado; orelhas/chifres se afastam do centro; cabelo de trás tem paralaxe invertida. Em partes `F.` (furry), focinho, nariz e boca têm reforço de profundidade. A simetria usa a posição das camadas, não L/R.

O painel **Editor de rig e deformação** (aba Preview, só com camadas) tem intensidades de 0 a 2 (geral, rosto, orelhas/chifres, cabelo, cabeça, olhar), liga/desliga, giro de teste manual e **Salvar calibração** / **Restaurar padrão**. Os valores ficam no projeto (`rig.settings`); valores inválidos voltam ao padrão. O clique no olho acerta o olho já deformado.

Limitações: é projeção 2D de camadas rígidas (deslocamento + achatamento), não 3D real nem malha. Camadas sem papel reconhecido não deformam. Franja e cabelo lateral só têm paralaxe. Cílios não seguem o olhar. Não há detecção de espécie.

## Play e cenário

A aba **Play** (última da lista) abre uma janela separada, sem bordas, só com o VTuber escolhido. O cenário pode ser **Cor sólida** (presets de chroma key ou qualquer `#RRGGBB`) ou **Transparente** (só o personagem aparece). O cenário fica gravado no projeto (`scenery.settings`) ao clicar em Começar; cores inválidas são rejeitadas.

Na janela Play: duplo clique mostra ou esconde os botões **Mover** e **X** (Esc também fecha quando eles estão visíveis); segure e arraste o botão **Mover** (ou use o botão direito + arraste em qualquer ponto) para mover a janela; a alça no canto redimensiona. Clique nos olhos pisca. O Play segue a câmera, não o mouse (ver Laboratório de movimento).

Limitações: em modo transparente, áreas totalmente vazias do desenho podem deixar o clique passar para a janela de trás. **O Play segue a câmera, não o mouse:** ao iniciar, ele liga a câmera (a primeira da lista, com a ferramenta e a calibração salvas no Laboratório de movimento) e a desliga ao fechar o Play. Se a câmera falhar ou ainda estiver carregando, um aviso aparece na janela e o VTuber fica parado, sem recorrer ao mouse. Para escolher outra câmera, ligue-a antes no Laboratório.

**No OBS:** use **Captura de Janela** (Window Capture), não "Captura de Jogo" — o app é uma janela WPF, não um jogo DirectX/OpenGL, então a Captura de Jogo não enxerga nada. Escolha a janela do Play na lista (com o Play aberto). Se o fundo transparente sair preto, troque o "Método de captura" para *Windows 10 (1903 e superior)* ou *BitBlt*; se ainda assim falhar, use o cenário **Cor sólida** (verde) e aplique o filtro **Chroma Key** na fonte.
## Laboratório de movimento (rastreamento facial)

A aba **Laboratório de movimento** mostra a câmera de um lado e o VTuber do outro. Escolha a ferramenta de detecção (hoje só **MediaPipe Face Mesh**, local), a câmera e toque em **Ligar câmera**. O VTuber copia: giro e inclinação da cabeça, abertura da boca e cada olho separadamente (piscar só um olho fecha só um olho do avatar).

- Tudo roda no computador: a câmera é lida via Windows (MediaCapture) e a detecção usa ONNX Runtime na CPU com os modelos da pasta `Models` (detector BlazeFace + Face Landmarker de 478 pontos, licença Apache 2.0, ver `Models\LICENSE-mediapipe-face-mesh-onnx.txt`). Nenhuma imagem é enviada para fora. A câmera é desligada ao sair da aba e ao fechar o app.
- **Espelhar** (ligado por padrão) faz o VTuber agir como um espelho; desligado, o olho direito seu fecha o olho direito do VTuber. O lado do olho do avatar vem da posição na imagem (esquerda/direita da tela), não do sufixo L/R.
- **Calibrar rosto neutro** (olhos abertos, boca fechada, olhando para a tela) define o ponto zero de giro/inclinação, o tamanho do olho aberto, a boca fechada e o centro da íris. Também há força da cabeça, força da boca, força da íris e suavização. **Salvar ajustes** grava em `tracking.settings` no projeto.
- **Íris (olhar):** a posição da íris dentro de cada olho é medida pelos pontos 468–477 da malha e move a pupila/íris do avatar independentemente da cabeça (média dos dois olhos; medida no eixo do rosto, então funciona com a cabeça girada). O slider **Força do olhar da íris** ajusta o efeito (0 = só a cabeça move os olhos).
- Boca: se o PSD tiver uma camada `mouthopen` (aliases: `openmouth`, `bocaaberta`…), ela aparece por transição conforme a boca abre (mesmo oculta no PSD). Sem ela, a camada `mouth` é tratada como boca aberta e **a própria arte é deformada** (sem sumir): ao fechar, ela se achata e se curva em **V invertido (Λ), preso à linha do focinho**, em personagens furry (prefixo F ou camada de focinho), ou em **curva deitada (C)** em humanos; ao abrir, volta suavemente à forma original. Desmarque **Fechar a boca com deformação** no editor de rig se a arte da sua boca já for fechada; nesse caso a boca apenas se estica.
- **Sobrancelhas:** a altura de cada sobrancelha em relação ao canto dos olhos (pontos 105/66/107 e 334/296/336, medida no eixo do rosto, então piscar não a move) sobe e desce as camadas `eyebrow` do avatar. Elas só se mexem **depois de calibrar** com a expressão relaxada (isso grava o repouso); o slider **Força das sobrancelhas** ajusta o efeito (0 = paradas).
- Corpo acompanha: a cabeça gira em torno do **pescoço** (base da camada `head`) e leva junto o movimento do corpo, então não se separa ao inclinar ou olhar para os lados. Camadas `body`, `neck` e `tail` deslizam e inclinam levemente (slider **Corpo acompanha** no editor de rig; 0 deixa o corpo parado). O fundo continua fixo.

Limitações: as sobrancelhas ainda não foram validadas com um rosto real (a sensibilidade e o sentido com Espelhar ligado/desligado precisam de conferência manual); precisão e latência dependem da câmera e da iluminação e ainda não foram medidas com um rosto real; o ponto do pescoço é estimado pela base da camada `head` (sem camada `head`, pelo rosto), então PSDs com a cabeça sobreposta a um pescoço muito diferente podem precisar de ajuste no slider; o achatamento da boca é feito em faixas verticais da imagem (não é malha), então em bocas muito detalhadas o formato é aproximado; os olhos do avatar seguem a cabeça e a íris, mas a íris é a parte menos precisa do rastreamento (webcams de baixa resolução, óculos e pouca luz pioram o resultado; o movimento vertical é o mais fraco e fica perto das pálpebras); o publish em arquivo único precisa ser validado com `onnxruntime.dll` e a pasta `Models`.

## Importação de PSD com camadas

Além de PNG/JPEG, é possível importar **PSD** (RGB, 8 bits por canal, até 500 MiB e 100 camadas, até 12.000 × 12.000 px) pelo mesmo seletor em Criar VTuber. Imagens acima de 40 MP são exibidas em resolução reduzida (4096 px) para poupar memória; os arquivos do projeto mantêm a resolução total. O leitor é próprio (sem dependências) e valida cabeçalho, tamanhos, limites e compressão (raw, RLE e ZIP); arquivos corrompidos, PSB, CMYK/16 bits ou sem camadas são recusados com mensagem. Ao confirmar:

- as camadas viram PNGs separados em `resources/layers/` do projeto, listadas em `psd.layers`;
- uma composição das camadas visíveis (opacidade respeitada) vira a imagem principal;
- camadas visíveis chamadas “olho”/“eye” (exceto “fechado”, “closed”, “sobrancelha” etc.) viram áreas de olho no preview; ajuste com **Marcar olhos** se necessário.

Limitações: modos de mesclagem, máscaras, camadas de ajuste/efeitos e recorte são ignorados (tudo tratado como “normal”), grupos não são preservados e o PSD original não é copiado nem alterado. O olho fechado por camada ainda não é usado no piscar.

## Early access, temas e integrações

- Versão `0.1.0-earlyaccess` (aparece no rodapé da barra lateral e em Configurações, junto das pastas de dados e logs).
- **Temas:** a aba **Temas** oferece **Azul escuro** (padrão), **Rosa** e **Branco**. A escolha é gravada em `%LOCALAPPDATA%\VTuberStudioAI\settings.json` e **vale ao reiniciar o app** (o app oferece reiniciar na hora). Arquivo ausente, corrompido ou com tema desconhecido volta ao azul escuro. Não há editor de cores livre nesta versão.
- **Integrações:** marcadas como **EM BREVE**; nada nelas está ativo e nenhuma conexão externa é feita. Para transmitir hoje, capture a janela da aba Play (cenário transparente ou colorido).
- Limitações do early access: sem validação com rosto/câmera reais das sobrancelhas, da íris e do corpo; o publish em arquivo único (`onnxruntime.dll` + `Models`) precisa ser conferido em outro computador; sem assinatura de código (o Windows SmartScreen pode avisar); WebP não suportado.

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
- Páginas de roadmap identificadas como não implementadas (Integrações: "em breve").
- Área de estúdio com estado vazio explícito.
- Tratamento de exceções globais e registro local de erros.

Os logs são gravados em `%LOCALAPPDATA%\VTuberStudioAI\logs`.

## Planejado

Movimento, rigging, renderização e preferências do aplicativo.

## Limitações conhecidas

- A biblioteca grava metadados JSON em `%LOCALAPPDATA%\VTuberStudioAI\Projects`, um diretório por identificador.
- WebP não é suportado; as configurações furry são metadados e não deformam ou animam imagens; não há animação 3D real nem integração com OBS/Live2D (o rastreamento facial só existe no Laboratório de movimento). A interface do diálogo de confirmação e o ciclo completo de reinicialização do app ainda aguardam teste manual.

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

## Licença e contribuição

Código sob a [licença MIT](./LICENSE). Veja [CONTRIBUTING.md](./CONTRIBUTING.md) para contribuir e [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md) para as licenças de terceiros.

