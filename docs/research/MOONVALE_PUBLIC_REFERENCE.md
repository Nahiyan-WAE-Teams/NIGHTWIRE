# Public Reference Research: Moonvale

- Research date: 2026-11-14
- Purpose: identify publicly documented product patterns that can inform NIGHTWIRE's original design.
- Boundary: this records public product information only. It does not claim access to Everbyte's source code, private APIs, internal architecture, or proprietary episode formats.

## Sources

1. Everbyte, [Moonvale Episode Tracker](https://everbytestudio.com/moonvale-episode-tracker/)
2. Everbyte, [Moonvale product page](https://everbytestudio.com/moonvale-detective-story/)
3. Everbyte, [Moonvale support and FAQ](https://everbytestudio.com/moonvale-support-faq/)
4. Everbyte, [Moonvale feedback and product decisions](https://everbytestudio.com/moonvale-feedback/)
5. Google Play, [Moonvale: Murder Mystery Game](https://play.google.com/store/apps/details?id=com.everbytestudio.interactive.story.moonvale)

## Publicly documented product patterns

### Messenger as the primary story surface

Everbyte describes Moonvale as an interactive mystery experienced through a messenger. Its product page lists text conversations, voice messages, pictures, videos, calls, and decisions as parts of the experience.

**NIGHTWIRE application:** make the conversation timeline a first-class runtime surface. Message rendering should be driven by typed episode records and stable media IDs, not hard-coded story screens.

### Character profiles as narrative surfaces

The Episode Tracker describes social-media-style character profiles containing biographies, interests, favorite links, and personal posts. It frames profiles as another way to reveal character personality and information outside direct conversations.

**NIGHTWIRE application:** character profiles should be data-driven pages with localized fields, optional interests/links, authored posts, comments, and references to episode media.

### A player-owned media collection

The tracker describes a Media Collection for images, calls, voice messages, and other received investigation material, organized so players can revisit it.

**NIGHTWIRE application:** build a media archive that indexes received/unlocked media across conversations and calls. The archive is a runtime projection of episode asset metadata plus player unlock state; it must not duplicate or modify the original downloaded media.

### Prerecorded calls and media playback

Everbyte publicly describes calls and media as part of the messenger experience. Public sources do not document their internal encoding, scheduling format, or playback implementation.

**NIGHTWIRE application:** keep prerecorded call definitions separate from chat messages. A call invitation references a call ID; the call definition points to local episode media, subtitle tracks, and allowlisted presentation cues. Playback must work offline after package installation.

### Player profile and customization

The tracker describes work on player-profile presentation, profile/background collections, achievements, and the ability to reduce or hide achievement notifications. It also discusses messenger themes as a customization feature.

**NIGHTWIRE application:** separate player-owned profile/save state from immutable episode character definitions. Themes should be presentation configuration selected by the player, not story content. Achievement visibility and reduced-motion settings belong in player preferences.

### Minigame atmosphere and accessibility

The tracker discusses a minigame redesign intended to better match a darker story tone while preserving clear geometric shapes and accessible distinctions. It notes that color alone should not carry the meaning of puzzle pieces.

**NIGHTWIRE application:** puzzle types are implemented by the runtime and configured by episode data. Every puzzle needs shape, label, or symbol cues in addition to color, plus input alternatives and clear feedback.

### Progression and save reliability

Everbyte's tracker discusses a more robust online save system and a staged rollout. Its support page documents save/load and account linking. This confirms that save reliability is an important product concern, but does not disclose the underlying save protocol.

**NIGHTWIRE application:** local saves must be authoritative for offline play. Add a versioned save envelope and explicit migration path. Treat cloud sync as an optional later service; never make the story runtime depend on a network round-trip to advance.

### In-game navigation and next-action guidance

Everbyte's support page documents an Apps tab and a phone app used in a troubleshooting flow. It also explains that a red notification indicator can point players toward the next required action, such as a minigame or chat overview.

**NIGHTWIRE application:** use an in-game phone/app launcher as a navigation hub for Messenger, Contacts, Media, Calls, Evidence, and Settings. Notifications should route to an actionable destination and remain accessible without relying on color alone.

## What these sources do not establish

The public material does not reveal:
- Moonvale's Unity scene hierarchy, prefab names, source code, or internal class design.
- Its exact episode archive format, manifest schema, download endpoints, or content serialization.
- Whether all media and conversation data are stored together or downloaded through the same request.
- Its exact internal event scheduler, save database, or call synchronization logic.

NIGHTWIRE must use its own schemas, runtime implementation, assets, characters, and story. Public product behavior is a reference for feature planning, not evidence of Moonvale's private implementation.

## Implementation consequences

The next client architecture milestone should define reusable runtime modules for:
1. Episode package installation and verified local asset access.
2. Story/event scheduler and persistent progression.
3. Messenger timeline, group conversations, typed messages, and choice handling.
4. Character directory and profile/post surfaces.
5. Media archive and unlock index.
6. Prerecorded call player with subtitles and presentation cues.
7. In-game app launcher, notification routing, and evidence surfaces.
8. Declarative minigame host with accessibility requirements.
9. Player profile, themes, settings, and versioned save state.

These modules should consume episode data and share one navigation/state model. A new episode should add content, not duplicate these systems.
