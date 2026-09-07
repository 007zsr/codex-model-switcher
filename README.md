# Private Encrypted Codex Transfer

This private repository is a temporary transport point for an encrypted migration payload.

The repository does not contain the migration key and does not contain plaintext personal files. The encrypted payload is attached to release `transfer-20260907-0fff7d79` as `payload.enc`.

## Restore On A New Windows Computer

1. Log in to GitHub CLI with an account that can read this private repository:

```powershell
gh auth login
```

2. Clone this repository:

```powershell
gh repo clone 007zsr/codex-transfer-20260907-0fff7d79
cd codex-transfer-20260907-0fff7d79
```

3. Run the restore script:

```powershell
powershell -ExecutionPolicy Bypass -File .\restore_from_github.ps1
```

The script will ask for the migration key. The key was saved only on the source computer and was not uploaded.

After a successful restore, the script deletes the GitHub release and tag that contain the encrypted payload, then attempts to delete this temporary repository. If repository deletion is not allowed by the current GitHub token, delete the repository manually after confirming the data has restored.

## Safety Notes

- The script never deletes local source or destination files.
- If a destination folder already exists, restored files are copied to a timestamped sibling folder.
- A scheduled GitHub Actions cleanup workflow is included as a fallback to remove the encrypted release after the expiry time.

