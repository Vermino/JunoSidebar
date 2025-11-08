# Quick Start: Wake Word Testing

## The Problem You're Having

Your debug logs show Whisper is hearing:
- ❌ "What time is it?" - **NO wake word**
- ❌ "Visit www.transcribe.com/transcribe.com" - **Whisper hallucination (silence)**
- ❌ "You know, what time is it?" - **NO wake word**

**None of these contain "Hey Juno"**, so the wake word is never detected!

## What You MUST Say

```
"HEY JUNO, what time is it?"
    ↑↑↑
  Say this FIRST!
```

## Correct Examples

✅ **"Hey Juno, what time is it?"**
✅ **"Hi Juno, help me"**
✅ **"Hello Juno, check my email"**

## What the Logs Should Show

### When Wake Word is DETECTED ✓

```
[Voice] Wake word check transcription: "Hey Juno, what time is it?"
[Voice] ========================================
[Voice] ✓✓✓ WAKE WORD DETECTED! ✓✓✓
[Voice] ========================================
[Voice] Starting command recording...
[Conversation] Wake word detected event received - setting state to Listening
[Conversation] Assistant state changing: Idle → Listening
```

Then you'll see the UI change:
1. **Mic button lights up blue** (listening state)
2. **Audio bars animate** as you finish speaking
3. **"Processing..."** indicator shows
4. **Sidebar auto-expands** when responding
5. **Response appears**

### When Wake Word is NOT DETECTED ✗

```
[Voice] Wake word check transcription: "What time is it? [BLANK_AUDIO]"
[Voice] Wake word NOT found. Transcription: "what time is it? [blank_audio]" | Looking for: "hey juno", "hi juno", "hello juno"
```

**Nothing happens in the UI** - This is what you're experiencing now!

## Step-by-Step Testing

1. **Power ON** (blue button in app)
2. **Wait 2 seconds** (let microphone initialize)
3. **Speak clearly and loudly:**
   ```
   "HEY JUNO... what time is it?"
   ```
4. **Watch debug logs** - You should see the "✓✓✓ WAKE WORD DETECTED! ✓✓✓" banner
5. **Watch the app UI** - Should show listening → processing → responding

## Tips for Being Heard

### Volume
- Speak **louder** than normal conversation
- Microphone volume should be **70-90%** in Windows settings
- Position yourself **1-2 feet** from microphone

### Pronunciation
- Say "**HEY**" clearly (not "hay" or "eh")
- Say "**JU-NO**" with two distinct syllables (not "juno" slurred together)
- **Pause slightly** after "Hey Juno" before saying your command

### Pacing
```
"HEY JUNO" [pause 0.5 seconds] "what time is it?"
```

This helps Whisper separate the wake word from the command.

## Common Mistakes

### ❌ Saying the command without wake word
```
You: "What time is it?"
Whisper hears: "What time is it?"
Result: Nothing happens (no wake word detected)
```

### ❌ Mumbling "Hey Juno"
```
You: "hmm juno what time is it?"
Whisper hears: "you know what time is it?"
Result: Nothing happens (wake word not recognized)
```

### ❌ Speaking too quietly
```
You: (whisper) "hey juno what time is it?"
Whisper hears: "[BLANK_AUDIO]" or hallucinations
Result: Nothing happens (no clear audio)
```

### ✅ Correct Usage
```
You: "HEY JUNO, what time is it?"
Whisper hears: "Hey Juno, what time is it?"
Result: ✓✓✓ WAKE WORD DETECTED! ✓✓✓
```

## If It Still Doesn't Work

1. **Check the transcription** in debug logs - What is Whisper actually hearing?
2. **Try being louder** - Increase microphone volume in Windows settings
3. **Try these exact phrases:**
   - "Hey Juno, help me"
   - "Hi Juno, hello"
   - "Hello Juno, what time is it"
4. **Check microphone selection** - Make sure you're using your headset microphone, NOT "Stereo Mix"

## Debug Log Analysis

Look for this pattern in your logs:

### Good Pattern (Wake Word Working)
```
[VAD] Speech started (energy: 0.0667, threshold: 0.0300)
[VAD] Speech ended (silence frames: 15)
[Voice] Checking for wake word in 70400 bytes of audio...
[Whisper] Transcription complete: "Hey Juno, what time is it?"
[Voice] ========================================
[Voice] ✓✓✓ WAKE WORD DETECTED! ✓✓✓
[Voice] ========================================
```

### Bad Pattern (No Wake Word)
```
[VAD] Speech started (energy: 0.0667, threshold: 0.0300)
[VAD] Speech ended (silence frames: 15)
[Voice] Checking for wake word in 70400 bytes of audio...
[Whisper] Transcription complete: "What time is it? [BLANK_AUDIO]"
[Voice] Wake word NOT found. Transcription: "what time is it? [blank_audio]" | Looking for: "hey juno", "hi juno", "hello juno"
```

## Next Steps After First Success

Once you see "✓✓✓ WAKE WORD DETECTED! ✓✓✓" in the logs, you should immediately see:
1. UI mic button lights up
2. State changes to "listening"
3. After you finish speaking, state changes to "processing"
4. Then state changes to "responding"
5. Sidebar expands and shows response

If you see the wake word detected in logs but NO UI changes, then we have a different problem (UI message passing). But first, let's get the wake word detected!

## Remember

**You MUST say "Hey Juno" before EVERY command.** The system is always listening for this phrase. Without it, Juno stays idle and ignores everything else you say.

This is by design - it prevents Juno from responding to every random conversation you have!
