# Spike: MSAL Token Cache in 1Password

**Goal:** Prove that MSAL.NET's token cache can be serialized to/from a 1Password Secure Note, enabling unattended agent access to Microsoft Graph without re-authenticating.

## Success Criteria

| # | Criterion | How to verify |
|---|---|---|
| 1 | Interactive login stores refresh token in 1Password | Run once → check `op item get msal-token-cache --vault msgraph-cli` |
| 2 | Second run acquires token silently (no browser) | Run again → stderr shows "Token acquired SILENTLY" |
| 3 | Access token works for Microsoft Graph | stdout contains JSON with your `displayName` and `email` |

## Prerequisites

1. **.NET 10 SDK** — [Install](https://dotnet.microsoft.com/download/dotnet/10.0)
2. **1Password CLI (`op`)** — [Install](https://developer.1password.com/docs/cli/get-started/)
3. **Entra ID App Registration** — see setup below

## Setup

### 1. Create the Entra ID App Registration

1. Go to the [Azure Portal → App Registrations](https://portal.azure.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)
2. Click **New registration**
3. Configure:
   - **Name:** `msgraph-cli`
   - **Supported account types:** Accounts in this organizational directory only (Single tenant)
   - **Redirect URI:** Select "Public client/native (mobile & desktop)" → `http://localhost`
4. After creation, go to **Authentication**:
   - Ensure **Allow public client flows** is set to **Yes**
5. Go to **API permissions** → **Add a permission** → **Microsoft Graph** → **Delegated permissions**:
   - `User.Read`
   - `Mail.Read` (for future phases)
   - `offline_access`
6. Click **Grant admin consent** (or consent will be requested at first login)
7. Note your **Application (client) ID** and **Directory (tenant) ID** from the Overview page

### 2. Store Credentials in 1Password

**Option A: Use the setup script**

```bash
chmod +x setup-1password.sh
./setup-1password.sh --client-id YOUR_CLIENT_ID --tenant-id YOUR_TENANT_ID
```

**Option B: Use the app's built-in setup**

```bash
cd MsalOnePasswordSpike
dotnet run -- --setup
```

**Option C: Manual**

```bash
# Create vault
op vault create msgraph-cli

# Store app registration
op item create \
  --category Login \
  --title "app-registration" \
  --vault msgraph-cli \
  "client-id=YOUR_CLIENT_ID" \
  "tenant-id=YOUR_TENANT_ID"
```

## Run the Spike

### First Run (Interactive Login)

```bash
cd MsalOnePasswordSpike
dotnet run
```

This will:
1. Read client ID and tenant ID from 1Password
2. Open your browser for Microsoft login
3. After consent, store the MSAL token cache in 1Password
4. Call `GET /me` on Microsoft Graph
5. Print your profile as JSON to stdout

**If running headless / via SSH:**

```bash
dotnet run -- --device-code
```

This shows a code and URL. Open the URL on any device, enter the code, and sign in.

### Second Run (Silent — The Key Test)

```bash
dotnet run
```

Expected behavior:
- **No browser opens**
- stderr shows: `[MSAL] ✓ Token acquired SILENTLY from cache (no browser needed).`
- stdout shows your profile JSON with `"tokenSource": "cache (silent)"`

This proves the cache round-trip through 1Password works.

### Other Commands

```bash
# Check auth status
dotnet run -- --status

# Clear tokens (logout)
dotnet run -- --logout

# Re-run setup
dotnet run -- --setup
```

## Architecture

```
Program.cs
    │
    ├── OnePasswordStore.cs          Wraps `op` CLI for read/write
    │     └── Calls: op read, op item get, op item create, op item edit
    │
    └── OnePasswordTokenCacheHelper.cs   Bridges MSAL ↔ 1Password
          ├── BeforeAccess: Read base64 blob from 1Password → DeserializeMsalV3
          └── AfterAccess:  SerializeMsalV3 → Write base64 blob to 1Password
```

**Token cache format:**
- MSAL serializes its cache as a `byte[]` (MSAL V3 format, which is JSON internally).
- We Base64-encode the byte array and store it in a 1Password Secure Note's `notesPlain` field.
- On load, we Base64-decode and pass to `DeserializeMsalV3`.

**Why Base64?**
- The MSAL cache blob is JSON but can contain characters that cause issues with `op` CLI argument parsing.
- Base64 ensures the blob is a single safe string with no escaping concerns.
- The overhead is minimal (~33% size increase on an already-small cache).

## What's in the 1Password Vault After Running

| Item | Type | Contents |
|---|---|---|
| `app-registration` | Login | `client-id`, `tenant-id` fields |
| `msal-token-cache` | Secure Note | Base64-encoded MSAL V3 cache blob containing access token, refresh token, and account metadata |

## Findings / Go-No-Go

After running the spike, document:

- [ ] Did silent token acquisition work on the second run?
- [ ] How long did the `op` CLI calls take? (Check `--verbose` output)
- [ ] Was the Base64 blob size reasonable? (Check `op item get msal-token-cache --vault msgraph-cli`)
- [ ] Did token refresh work? (Wait for access token to expire, ~1 hour, and run again)
- [ ] Any issues with `op` stdin for large blobs?

**Go:** Proceed with the architecture as designed (MSAL cache blob in 1Password Secure Note).

**No-Go:** If `op` CLI latency is unacceptable or blob storage hits limits, consider:
- Alternative A: Store only the refresh token manually, bypass MSAL cache serialization.
- Alternative B: Use a local encrypted file as primary cache, sync to 1Password as backup.
