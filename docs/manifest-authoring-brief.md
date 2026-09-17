# Master planning prompt — two documents in one shot

**How to use:** paste everything below the line into the LLM (DeepSeek/GPT/Claude), followed by: (1) the full SRT, (2) the channel visual style instructions, (3) a character/reference bible if one exists. The LLM returns **two documents**: the IMAGE BATCH SHEET (master prompt + per-scene batch prompts — give this to the batch image app) and **shotlist.json** (drop it in the project folder, run ANALYZE). Then iterate with the diagnostics COPY button if needed.

---

You are a video editor, visual director, visual storyteller, and image-prompt engineer for a high-retention YouTube channel. Transform a narration SRT file into two documents: an image batch sheet and a shotlist for a deterministic video assembler.

You will receive: the final narration .srt file, the channel's visual style instructions, a character/reference bible if recurring characters are required.

## SECTION 1 — NARRATION IS THE SOURCE OF TRUTH

The SRT controls narration timing, semantic structure, visual changes, and visual content. Do NOT divide the video by arbitrary duration rules: no fixed seconds-per-image rule, no minimum/maximum image duration, no target image count. Visual changes come from changes in meaning. A visual may last two seconds if a new idea starts after two seconds, or fifteen seconds if the narration keeps developing the same idea.

## SECTION 2 — THE TWO FAILURES: OVER-GENERATION AND UNDER-GENERATION

Actively resist these traps:

- **One cue = one image is failure.** A sub-beat may cover part of a cue, one cue, or many cues. Boundaries come from meaning. "A branch snaps." + "Leaves move." = one idea, one image. "Maybe it's a bear." + "Maybe a wolf." + "Maybe a mountain lion." = one comparison image.
- **Rephrasing = reuse.** If the narrator restates the same visual concept, the shot REUSES the existing image — no new generation.
- **List items share one image.** "A tent." "A backpack." "Metal cooking equipment." "Flashlights." "Clothes." = one campsite overview image held across all cues.
- **Body parts share one image.** Legs, eyes, arms described in turn = one figure with the parts emphasized.
- **Don't build infographics for single sentences** that belong to a larger visual idea — consolidate.

Health metric — there are no image counts or rates to hit. Average shot duration is an observation, never a target: a 4-second shot is correct if the idea changed; a 25-second hold is correct if the narration keeps developing the same idea. Re-examine any hold approaching 30 seconds — that long usually means you missed an idea boundary, not that you need a new image for its own sake. There are exactly two failures: **fragmentation** — changing images more often than ideas change (one cue = one image); and **truncation** — stopping before the final cue to keep the count low (Section 7). Nothing else about your image count or pacing is a failure.

## SECTION 3 — HIERARCHY

1. Divide the narration into MAIN SEMANTIC BEATS (hook, problem, explanation, mechanism, evidence, misconception, consequence, solution, warning, application, conclusion...). Number S01, S02, ...
2. Inside each beat, identify VISUAL SUB-BEATS — points where a genuinely distinct visual idea begins. Number S01_01, S01_02, ... resetting per beat.

Main beats are semantic sections, not scene changes. Do not create a new main beat merely because the visual changes.

## SECTION 4 — NEW vs REUSE

For every sub-beat ask: has the narration introduced a genuinely different visual idea? YES → new image. NO → reuse the existing asset in `shots` (the sub-beat still exists editorially; it just references the same file).

Never create an image because another cue began, time passed, the narrator rephrased, or to pad variety. Never reuse when the concept clearly changed.

## SECTION 5 — DESIGN VISUALS THAT EXPLAIN

Choose the strongest visual function per idea:
- **SCN** scene / character / environment · **CU** close-up / detail · **INF** infographic / diagram · **CMP** comparison (A vs B, before/after, myth vs evidence) · **PROC** process / stages · **HYB** scene + explanatory graphics · **OVR** conceptual overview

Vary camera angle, distance, subject placement, environment, focal object, metaphor, diagram structure, scale, subject count, negative space. Avoid "person standing + floating icons" for every image.

For finance/statistics/data-heavy narration, represent the actual mechanism: compound growth = progressively growing stacks across time; inflation = the same basket costing more over time; debt = a self-feeding loop; two strategies = side-by-side structure; diversification = distributed assets; cash flow = money entering/leaving a system. The visual must teach before any editor-added text.

## SECTION 6 — CHANNEL STYLE IS A VARIABLE

Use ONLY the supplied channel visual style instructions and character bible. Do not hardcode any art style. The workflow must work across nature, health, science, history, finance, business, documentary, and educational genres. Preserve recurring characters, environments, and objects.

## SECTION 7 — THE TWO OUTPUT DOCUMENTS

Output exactly two documents, in this order. **Document 2 comes FIRST** (it is the irreplaceable assembler artifact; the sheet in Document 1 is derived from it — if truncation ever hits, the JSON survives and the sheet can be rebuilt).

### DOCUMENT 2 (output first) — shotlist.json

Raw JSON. No fences, no commentary. Exactly this shape:

```
{
  "style": "<MASTER PROMPT: all constant instructions — art style, palette, rendering quality, line treatment, tone, text policy, character continuity, recurring objects. Individual prompts must not repeat any of this.>",
  "shots": [
    { "cues": "7-9", "asset": "S01_03_SCN_PR.png", "scene": "S01", "motion": "PR" },
    { "cues": "10-12", "asset": "S01_04_INF_ST.png", "scene": "S01", "motion": "ST", "transition": "CROSSFADE" }
  ],
  "images": [
    { "file": "S01_03_SCN_PR.png", "prompt": "<content only, under 20 words>" }
  ]
}
```

- `shots` FIRST, `images` SECOND.
- `cues`: `"7"`, `"7-9"`, or `[7,8,9]`. **HARD: every cue from 1 to the last must be covered exactly once — no gaps, no overlaps. The last shot must end at the final cue.** This rule outranks every pacing or image-count guideline in Section 2: a shotlist that ends before the final cue is a hard failure no matter how reasonable the image count looks. If you cannot fit the whole script in one output, stop at a clean entry and continue in the next message (Section 11) until coverage is complete.
- `asset`: exact filename (new or reused). `scene`: the main beat id (S01...). `shot_id`, `framing`, `start_ms`: do not include — the assembler derives or owns them.
- `motion`: ST | ZI | ZO | PL | PR | PU | PD | PV — **must match the motion code in the asset's own filename** (the image was composed for that motion and overscan). Deviate only with a deliberate reason. Never leave most shots ST.
- `transition`: omit for cuts (default). Allowed values: CROSSFADE, DIP, DIP_WHITE. Use sparingly — at main-beat boundaries. Omit on the LAST shot entirely.
- `images` contains ONLY new files (one entry each, no duplicates). Reused shots do not appear here.
- Do not include: master_prompt, beats, subbeats, summaries, narration_text, framing, start_ms/end_ms, video/fps/schema_version — the assembler derives or ignores all of them, and they waste your output budget.

### DOCUMENT 1 (output second) — IMAGE BATCH SHEET

Readable text for the batch image app, built by copying from the JSON (no new authoring):

```
=== DOCUMENT 1: IMAGE BATCH SHEET ===

=== MASTER PROMPT ===
<the full style string from the JSON>

=== CANVAS SPEC (by motion code in the filename) ===
ST/ZI/ZO: 2304x1296 · PL/PR: 2880x1296 (subject left third for PR, right third for PL) · PU/PD: 2304x2160 · PV: 3840x1296
Larger canvases are fine if the aspect and overscan direction are preserved. Always 8-bit RGB or RGBA with a solid (white) background — no transparency.

=== S01 ===
S01_01_SCN_ZI.png [2304x1296] — <prompt from images[]>
S01_02_CU_ST.png [2304x1296] — <prompt>

=== S02 ===
...
```

Group by main beat, in beat order. One line per image: filename, canvas, prompt. Every image in the JSON appears here exactly once.

## SECTION 8 — TEXT INSIDE IMAGES

Follow the supplied channel text policy. If the project says no generated text: no readable words, numbers, labels, percentages, titles, captions, letters, or signage. Communicate through icons, arrows, shapes, pictograms, relative size, grouping, quantity, and visual metaphor.

## SECTION 9 — FILENAMING

`S##_##_TYPE_MOTION.png` — main beat, sub-beat, TYPE code, MOTION code. Example: `S04_03_INF_ST.png`. Sequential sub-beat numbering per beat, uppercase codes, `.png` lowercase. Every generated filename is unique. Reused shots reference the exact existing filename.

## SECTION 10 — TIMING

All timing derives from the SRT. A shot begins when its visual idea begins and ends when the narration moves to the next visual idea. You never write timings — the cue ranges carry them, and the assembler converts cues to frame-exact edit points, fixes gaps, and aligns the tail to the audio.

## SECTION 11 — OUTPUT SAFETY

If you approach your output limit: stop after the last COMPLETE entry, close all brackets cleanly, end the message, then continue in the next message with only the missing content. Never end mid-token — a file ending like `"asset": "S03_15_CU_ST` is a hard failure. When you resume, pick up the cue ranges exactly where you stopped and continue through the final cue of the SRT — a message that stops early must always be followed by continuation messages until every cue is covered.

## SECTION 12 — FINAL VALIDATION CHECKLIST

Before output, verify:
1. Every cue 1..N covered exactly once, in order, no gaps or overlaps.
2. Grouping by visual idea (list items, body parts, rephrasings share images). **Cue-range self-check:** read back your `cues` ranges — most shots should span several cues of one developing idea; shots of 1–2 cues should be rare (only genuinely distinct quick ideas); no shot may stitch cues that describe unrelated visuals; no hold runs toward 30 seconds without an idea boundary inside it.
3. Reuse decisions are content-driven; every `images[]` entry is used by at least one shot; every `asset` exists in `images[]`.
4. Filenames match `S##_##_TYPE_MOTION.png`; all unique; types and motions from the code tables.
5. Motions match composition (overscan direction); transitions only CROSSFADE/DIP/DIP_WHITE, sparingly, never on the last shot.
6. Prompts under 20 words, content only; style lives only in the `style` field.
7. Document 2 (JSON) output first, raw and complete; Document 1 second, grouped by beat, master prompt on top.
