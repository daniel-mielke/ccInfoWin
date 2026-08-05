---
created: 2026-03-16T10:17:54.201Z
title: Display API errors as red banner with technical details
area: ui
files:
  - CCInfoWindows/CCInfoWindows/ViewModels/MainViewModel.cs
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
  - CCInfoWindows/CCInfoWindows/Services/ClaudeApiService.cs
---

## Problem

When API calls fail (e.g., network errors, Cloudflare blocks, invalid tokens), there is no visible
error feedback in the UI. Users have no idea what went wrong without checking logs.

Example error: `error sending request for url (https://claude.ai/api/organizations/.../usage)`

## Solution

- Add a red banner (e.g., `InfoBar` or custom `Border`) at the top of MainView with white text
- Show the actual error message including URL, HTTP status, and exception details
- Target audience is developers, so technical details are welcome — no need to sanitize
- Banner should auto-dismiss after ~10s or be manually closable
- Wire up error propagation from ClaudeApiService → MainViewModel → MainView binding
