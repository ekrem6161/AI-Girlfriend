# AIGirlfriend

The application source. See the [main README](../README.md) for the full
documentation.

```bash
dotnet run -- web     # web chat  -> http://localhost:5050
dotnet run -- test    # console test mode
dotnet run            # Telegram bot mode
```

| Folder | Contents |
|--------|----------|
| `prompts/` | `persona.md` — who she is |
| `atmosphere/` | `stage-01` … `stage-10` — her day, loaded as the day moves |
| `wwwroot/` | `index.html` — the chat interface |
| `config.json` | your settings (see `config.example.json`) |
