# Contribuindo com o VFtube AI

Obrigado pelo interesse! Contribuições são bem-vindas.

## Requisitos

- Windows 10 (19041) ou superior
- .NET SDK 10 (o projeto usa `net10.0-windows10.0.19041.0`)

## Compilar e testar

```powershell
dotnet restore .\VTuberStudioAI.slnx
dotnet build .\VTuberStudioAI.slnx
dotnet test .\VTuberStudioAI.slnx
dotnet run --project .\src\VTuberStudioAI.App\VTuberStudioAI.App.csproj
```

Mais detalhes (incluindo publicação) estão no [README](./README.md).

## Enviando mudanças

1. Faça um fork e crie uma branch a partir da principal.
2. Mantenha as mudanças pequenas e focadas em um único assunto.
3. Adicione ou atualize testes em `tests/VTuberStudioAI.App.Tests` quando alterar comportamento.
4. Rode `dotnet test .\VTuberStudioAI.slnx` e confirme que tudo passa.
5. Atualize o README se a mudança afetar o uso do app.
6. Abra um Pull Request explicando o que mudou e por quê.

## Issues

Ao reportar um bug, informe a versão do Windows, os passos para reproduzir e, se existir, o log do app.

## Licença

Ao contribuir, você concorda que sua contribuição será licenciada sob a [licença MIT](./LICENSE) do projeto.
Não envie imagens, avatares ou modelos sem ter o direito de distribuí-los.
