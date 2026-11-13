# RatCache: status against the SRS

Checked against `documentation/RatCache_SRS.docx` and `main` on 2026-11-12.
All remote branches are merged into `main`. Both test suites pass: 78 .NET tests and 42 CLI tests.

## Already done

- **Accounts (FR-1.1 to 1.6, 1.8):** register, sign in and out, 14-day session cookie, change password, delete account, and create and revoke API tokens. Works in the API and on the web pages.
- **Paste CRUD (FR-2.1 to 2.7, 2.9):** create, view, raw view, edit, delete, my pastes, and the copy and share buttons.
- **Visibility and expiry (FR-3.1 to 3.3, 3.5):** public, unlisted and private (private needs an account), all 6 expiry choices, and a background job that deletes expired pastes every 10 minutes.
- **Highlighting (FR-4.1 to 4.4):** language dropdown, all 14 languages, highlight.js and line numbers.
- **Web pages (FR-5.1, 5.2):** home, paste view, edit, my pastes and account.
- **CLI (FR-6.1 to 6.6):** every command in §5.3, plus `ping`.
- **Security and errors:**
  - AES-256-GCM encryption, with the key kept outside the database (NFR-1.2, 1.3).
  - No paste content or passwords in logs (NFR-1.4).
  - bcrypt with cost 12 and a 12-character minimum password (NFR-2.1, 2.2).
  - The same error for a wrong username or password (NFR-2.4).
  - Secure cookie flags and CSRF protection (NFR-2.5).
  - Only hashes of API tokens are stored (NFR-2.6).
  - Input validation: size limits, empty-paste check, HTML escaping, EF Core's parameterized queries and server-side checks (NFR-3.1 to 3.5).
  - Error handling: JSON error bodies, the same not-found page for missing, expired and private pastes, request IDs in logs, and CLI errors on stderr (NFR-4.1 to 4.5).
  - 10-character random paste IDs (NFR-5.2).
- **Tests (NFR-6.2):** every endpoint that exists so far has a test.

## To do, in order

### Must

1. [ ] **Remove or gitignore `out/`.** It's an untracked `dotnet publish` folder from Nov 7, so someone could commit it by accident.
2. [ ] **Recent pastes (FR-3.6).** `Recent.razor` is still a "Not implemented yet" placeholder, and `GET /api/pastes/recent` from §5.2 doesn't exist. This is the last placeholder and the last missing endpoint, so add its tests at the same time.
3. [ ] **A way to make someone an admin (FR-7.1).** Admin delete already works in the API and on the paste page, but nothing sets `User.IsAdmin`. A config setting or a one-off command would do.
4. [ ] **Deploy to UpCloud (NFR-1.1, NFR-1.3).** This is the only Must left, and it has the most unknowns, so do it early. It involves:
   - a domain and DNS
   - a reverse proxy with a Let's Encrypt certificate (TLS 1.2 or later) and HTTP-to-HTTPS redirects
   - the encryption key set as the `RatCache__EncryptionKey` environment variable
   - a fixed path for the SQLite database
   - a systemd service
   - forwarded-headers middleware, so the app sees the real HTTPS scheme and client IP

### Should

5. [ ] **Daily database backup (NFR-5.3).** It's only a Could, but it takes about 10 minutes (`sqlite3 .backup` plus cron). Set it up as soon as the server exists, before any new migration runs on real data.
6. [ ] **Rate limiting (NFR-3.6).** Allow 10 anonymous pastes per minute per IP, using ASP.NET's built-in rate limiter. It comes after deploying because behind the proxy it needs the forwarded client IP. Without it, every request looks like it comes from the proxy.
7. [ ] **Lockout after 5 failed sign-ins (NFR-2.3).** This changes the database schema. Decide now whether you'll do any of the Coulds that also need new columns: a disabled flag (FR-7.2), a burn-after-reading flag (FR-3.4) and a delete-token hash (FR-2.8). If you will, put them all in this one migration.
8. [ ] **Phone-width check (FR-5.3).** Check every page at 360 px. The my pastes table and the row of three dropdowns on the home page are the likely trouble spots.
9. [ ] **Load test (NFR-5.1).** Test with 50 concurrent users and check that 95% of create and view requests take under 500 ms. Run it against the deployed server once the features are in, so you measure the final build.

### Could (easiest first)

10. [ ] **Dark theme (FR-5.4).** CSS only, plus a dark highlight.js theme.
11. [ ] **Burn after reading (FR-3.4).**
12. [ ] **One-time delete link for anonymous pastes (FR-2.8).**
13. [ ] **Disable an account (FR-7.2).** Both the session check and the token check would need to reject disabled users, and there's no admin page yet.
14. [ ] **Password reset by email (FR-1.7).** This is the most work: an email field and an SMTP setup. The SRS says it's fine to drop it if there isn't time.

### Wrap-up

15. [ ] **Docs.**
    - **README:** change the "Early development" status, add a deployment section, and time a fresh clone to confirm setup takes under 30 minutes (NFR-6.1).
    - **SRS clean-up:** it still says "Draft", and §2.4 says the stack hasn't been chosen, which contradicts §6. NFR-3.5 mentions an email format, but there's no email field. §6 is still called "Open Questions".
    - **Tests:** make sure the new endpoints have tests (NFR-6.2).
