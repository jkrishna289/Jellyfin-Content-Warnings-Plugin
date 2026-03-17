# Changelog

All notable changes to the Jellyfin Content Warnings Plugin will be documented here.

## [1.0.0] - 2026-03-17

### Added
- Initial release
- Automatic content warning tagging using Groq AI
- Support for movies and TV shows (series level)
- 20 standardised content descriptors with `CW:` prefix
- Admin dashboard settings page with API key management
- Scheduled task "Process Content Warnings" for bulk library processing
- Auto-tagging of new items on library scan
- Smart skipping — already-tagged items are never re-processed
- Official MPAA/TV rating saved to the rating field if not already set
- Support for Groq models: `llama-3.3-70b-versatile`, `llama-3.1-8b-instant`
