# realmdump

```bash
dotnet run -c Release -- "/path/to/client.realm"
dotnet run -c Release -- "/path/to/client.realm" --classes Filter --out out.json
dotnet run -c Release -- "/path/to/client.realm" --classes Ruleset --query "Available == true"
```

```bash
dotnet publish -c Release -r osx-arm64 --self-contained true
```
