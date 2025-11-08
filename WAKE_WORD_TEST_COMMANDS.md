# Wake Word Test Commands

Use these test phrases to verify wake word detection and Whisper transcription accuracy.

## Wake Word Variations

Test that Juno recognizes different variations of the wake word:

1. **"Hey Juno"** - Standard wake word
2. **"Hi Juno"** - Alternative greeting
3. **"Hello Juno"** - Formal greeting
4. **"OK Juno"** - Alternative activation phrase

## Quick Test Commands

Short, clear commands to test basic functionality:

### Calendar & Schedule
- "Hey Juno, what's on my calendar today?"
- "Hey Juno, when is my next meeting?"
- "Hey Juno, schedule a meeting for tomorrow"
- "Hey Juno, show my appointments"

### Email
- "Hey Juno, check my email"
- "Hey Juno, do I have any new messages?"
- "Hey Juno, send an email to John"

### Files & Documents
- "Hey Juno, find my recent documents"
- "Hey Juno, open the quarterly report"
- "Hey Juno, search for the budget file"

### General Queries
- "Hey Juno, what time is it?"
- "Hey Juno, what's the weather today?"
- "Hey Juno, help me with my tasks"

### Tools & Actions
- "Hey Juno, open settings"
- "Hey Juno, show me available tools"
- "Hey Juno, run a search"

## Testing Tips

### 1. Microphone Quality
- Speak clearly and at normal volume
- Position yourself 1-2 feet from the microphone
- Minimize background noise
- Use a quality microphone (headset recommended over built-in laptop mic)

### 2. Wake Word Detection
- Pause slightly after saying "Hey Juno" before the command
- Example: "Hey Juno... [pause] ...what's on my calendar?"
- The pause helps Whisper separate the wake word from the command

### 3. Command Clarity
- Speak naturally but clearly
- Avoid mumbling or rushing
- Complete sentences work better than fragments

### 4. Common Transcription Issues

**If Whisper transcribes incorrectly:**
- Check microphone volume (should be 70-90% in system settings)
- Ensure you selected the correct input device (NOT Stereo Mix)
- Speak a bit louder or closer to the microphone
- Reduce background noise (close windows, turn off fans)

**If wake word isn't detected:**
- Ensure the power button is ON (blue indicator)
- Check that audio level bars are moving when you speak
- Try saying "Hey Juno" more clearly and slightly louder
- Verify the correct wake word in Settings

### 5. Testing Sequence

**Phase 1: Wake Word Only**
1. Say just "Hey Juno" and stop
2. Verify Juno activates (mic button lights up)
3. Wait for timeout (should complete after 10 seconds)

**Phase 2: Wake Word + Command**
1. Say "Hey Juno, what time is it?"
2. Verify Juno activates (listening state)
3. Verify processing state appears
4. Verify response is shown

**Phase 3: Manual Activation**
1. Click the microphone button
2. Say a command (without wake word)
3. Click the microphone button again to stop
4. Verify processing and response

## Expected Visual Feedback

When wake word is detected, you should see:

1. **Listening State** (immediately after wake word)
   - Microphone button lights up (blue/active color)
   - Audio level bars animate with your voice
   - Status shows "Listening..." or similar

2. **Processing State** (after you finish speaking)
   - Status shows "Processing..." or "Thinking..."
   - Audio bars stop animating
   - Loading indicator appears

3. **Responding State** (when Juno responds)
   - Sidebar auto-expands (if collapsed)
   - Response text appears
   - Status shows "Responding..." or conversation view

4. **Idle State** (after completion)
   - Returns to normal view
   - Microphone button no longer lit
   - Ready for next wake word

## Troubleshooting

### Audio Levels Not Showing
- ✓ Power must be ON
- ✓ Correct input device selected in Settings
- ✓ Microphone permissions granted (Windows settings)
- ✓ Microphone not muted in system

### Wake Word Not Detected
- Check Whisper transcription logs to see what was heard
- Try speaking louder/clearer
- Verify wake word setting matches what you're saying
- Ensure microphone volume is adequate (70-90%)

### Transcription Incorrect
- Whisper tiny model has limitations with accents/dialects
- Background noise affects accuracy
- Short utterances are harder to transcribe than full sentences
- Consider upgrading to Whisper base or small model for better accuracy

## Voice Testing Protocol

Follow this protocol to systematically test the wake word system:

### Session 1: Baseline (10 commands)
Test each of these 10 times and record success rate:
1. "Hey Juno, what time is it?"
2. "Hey Juno, check my email"
3. "Hey Juno, show my calendar"

### Session 2: Variations (10 commands)
1. "Hi Juno, what's the weather?"
2. "Hello Juno, help me"
3. "OK Juno, find files"

### Session 3: Complex Commands (5 commands)
1. "Hey Juno, schedule a meeting with Sarah for tomorrow at 3 PM"
2. "Hey Juno, search my emails for messages from John about the project"
3. "Hey Juno, what meetings do I have this week?"

### Success Criteria
- **Wake Word Detection**: >90% success rate
- **Command Transcription**: >80% accuracy for simple commands
- **Visual Feedback**: 100% (should always show listening/processing/responding)
- **End-to-End Flow**: >80% complete successfully

## Notes for Fine-Tuning

If accuracy is low (<80%), consider:

1. **Model Upgrade**: Switch from Whisper `tiny` to `base` or `small`
   - Tiny: Fastest, least accurate (~10MB)
   - Base: Good balance (~75MB)
   - Small: Better accuracy (~250MB)

2. **VAD Threshold**: Adjust voice activity detection sensitivity
   - Lower threshold = more sensitive (may pick up background noise)
   - Higher threshold = less sensitive (may miss soft speech)

3. **Audio Buffer**: Increase wake word audio buffer duration
   - Currently captures during VAD speech detection
   - May need to capture more context before/after speech

4. **Post-Processing**: Add fuzzy string matching for common misheard phrases
   - "hey June" → "Hey Juno"
   - "a Juno" → "Hey Juno"
   - "agent Juno" → "Hey Juno"
