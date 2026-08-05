---
created: 2026-03-16T10:25:00Z
title: Make vertical scrollbar always visible when content overflows
area: ui
files:
  - CCInfoWindows/CCInfoWindows/Views/MainView.xaml
---

## Problem

When the app window is resized to a small height, content is clipped but there is no visual
indication that the user can scroll. The default WinUI 3 ScrollViewer uses auto-hiding overlay
scrollbars that only appear on hover/touch — users don't realize there's more content below.

## Solution

- Set `VerticalScrollBarVisibility="Auto"` or `"Visible"` on the main `ScrollViewer` in MainView
- Consider using a non-overlay scrollbar style for persistent visibility:
  - Custom `ScrollViewer` style or template that keeps the scrollbar track always visible
  - Or a subtle fade/gradient at the bottom edge to hint at more content
- WinUI 3 option: `ScrollViewer.VerticalScrollBarVisibility="Visible"` forces the scrollbar to
  always show, but it uses overlay style by default — may need to also set the scrollbar to
  non-overlay via a style override
- Alternative: a small "scroll down" indicator or shadow at the bottom when content overflows
