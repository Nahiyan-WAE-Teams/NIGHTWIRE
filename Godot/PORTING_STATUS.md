# Godot Port Status and Feature Parity

Baseline inspected: `main` at branch creation. The current Unity implementation and episode content are the reference behavior. This file tracks actual parity; a feature is not complete merely because a C# runtime contract exists.

## Reference implementation inventory

### Existing episode/runtime behavior to preserve

- Versioned episode descriptor and manifest; JSON Schema and semantic validation.
- Safe package loading with manifest inventory, bounded sizes, path/link rejection, and SHA-256 verification.
- Character profiles, localized strings, conversations, authored choices, branches, relationship deltas, story flags, notifications, and event triggers.
- Deterministic timed message delivery and read/completion events.
- Separate player progress and immutable episode content.
- Versioned local saves, checksum validation, backup recovery, and explicit episode-content migration boundary.
- Episode library, signed release catalogue, trusted-key verification, anti-rollback sequence, HTTPS download, staged ZIP installation, interrupted-install recovery, offline discovery, and per-episode session/install exclusion.
- Asset unlock rules and verified access to image, audio, video, and document attachments.
- Prerecorded call timelines, synchronized subtitles/speaker/effect cues, resumable elapsed position, and completion triggers.
- Three investigation minigames: sequence reconstruction, frequency tuning, and evidence matching, including attempt/time state.
- Episode 001 authored content: Anisha, Rafi, Mira, linked conversations, branching relationship choices, story flags, and concluding investigation notification.

### Unity presentation already described as implemented

- Dark episode archive/library with offline discovery and install/update/refresh states.
- Messenger with localized message bubbles and authored choices.
- Conversation inbox for already-opened threads.
- Character contact directory and profile overlay.
- Social feed with localized posts/comments and verified image attachments.
- Evidence library with unlocked assets and bounded text/Markdown/JSON previews.
- Verified inline image and audio/video attachment playback.
- Prerecorded audio/video call presentation with timeline subtitles, speaker focus, visual cues, resume, and completion.
- Investigation entry and all three minigame presentations.
- Three procedural drifting fog layers behind archive and messenger.
- Session start/resume, save-backed return to archive, and lifecycle checkpoint behavior.

### Explicit Unity presentation gaps to implement in Godot

- Player language settings and language switching.
- Free-form replies (only if the product decision is confirmed; the current v0.1 episode contract is authored-choice based).
- Full PDF/office document viewing, or an explicit supported-format policy and graceful archive-only handling.
- Advanced searchable media-gallery filters.
- Full call-recording authoring/QA tooling is not a player-facing feature; preserve playback and synchronization behavior, and document any creator-tool scope separately.
- Physical-device verification of media decoder synchronization, audio/video drift, pause/resume, and background behavior.

## Godot implementation phases

1. **Project foundation:** pinned Godot 4.7.2 standard build, Android export configuration, app shell, theme, navigation, safe mobile layout, and separate CI definition (do not run CI before implementation/review milestone).
2. **Content/runtime parity:** port descriptor/manifest parsing, schema/semantic checks, safe package loader, localization, event interpreter, conversation interpreter, progress model, save store, and tests.
3. **Episode delivery:** signed catalogue verification, trusted-key policy, anti-rollback storage interface, bounded HTTPS downloader, safe staged ZIP installer, crash recovery, offline library, and session/install lock.
4. **Playable Episode 001:** archive, session start/resume, messenger, timed delivery, choices, story effects, inbox, contacts, profile, social feed, evidence, and save/return flow.
5. **Media and investigations:** verified image/audio/video/document access, call player/timeline, subtitles/cues/resume, all three minigames, and persisted attempt/timer state.
6. **Polish and accessibility:** atmosphere/fog, transitions, touch interactions, responsive layouts, text scaling, contrast, reduced motion, lifecycle handling, and performance/memory review.
7. **Release verification:** automated tests, static project validation, full Android APK export in independent GitHub Actions workflow, artifact inspection, then physical-device installation and smoke tests.

## Acceptance criteria

- No Unity file is modified by Godot implementation commits.
- Episode 001 uses the existing authored package; no hardcoded replacement dialogue or fabricated story.
- Every runtime feature has automated tests for normal and failure paths where practical.
- No feature is marked complete without implementation and test evidence.
- APK export includes the actual full playable flow, not a bootstrap-only or empty application.
- Unity and Godot build workflows are independent.
- Main branch is not changed or merged without explicit approval.

## Current status

- [x] Read-only inspection of repository main and Unity project documentation.
- [x] Dedicated `godot-port` branch created from `main`.
- [x] Godot isolation and scope documentation added.
- [ ] Godot project files and runtime implementation.
- [ ] Ported playable Episode 001.
- [ ] Godot tests.
- [ ] Godot Android CI workflow.
- [ ] Android APK and device verification.
