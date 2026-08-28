using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Media;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace Desktop_Frames
{
    // InterCore system for Desktop Frames - handles special interactive features and animations.
    public static class InterCore
    {
        #region Private Fields

        private static bool _isDanceActive = false;
        private static bool _isGravityActive = false;
        private static readonly Dictionary<StackPanel, Point> _originalIconPositions = new Dictionary<StackPanel, Point>();


        // --- NEW: Registry Listener Fields ---
        private static DispatcherTimer _registryMonitor;
        private static string _lastTriggerValue;
        // ------------------------------------

        #endregion

        #region Public Methods

        // Initializes the InterCore system - call this during application startup
        public static void Initialize()
        {
            try
            {
                // REGISTER HOTKEYS from GlobalHotkeyManager
                GlobalHotkeyManager.DancePartyTriggered += (s, e) => ActivateDanceParty();
                GlobalHotkeyManager.GravityDropTriggered += (s, e) => ActivateGravityDrop();

                // --- NEW: Start Registry Listener ---
                _registryMonitor = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1000) };
                _registryMonitor.Tick += CheckRegistryTrigger;
                _registryMonitor.Start();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore system initialized and subscribed to global hotkeys");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error during initialization: {ex.Message}");
            }
        }

        // Cleans up any active effects - call this during application shutdown
        public static void Cleanup()
        {
            try
            {
                _registryMonitor?.Stop(); // Stop listener
                _originalIconPositions.Clear();

                // Unsubscribe to prevent leaks
                GlobalHotkeyManager.DancePartyTriggered -= (s, e) => ActivateDanceParty();
                GlobalHotkeyManager.GravityDropTriggered -= (s, e) => ActivateGravityDrop();

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore system cleaned up");
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error during cleanup: {ex.Message}");
            }
        }

        private static void CheckRegistryTrigger(object sender, EventArgs e)
        {
            string currentValue = RegistryHelper.CheckForTrigger();

            // 1. If registry is empty, reset memory so we can accept new commands
            if (string.IsNullOrEmpty(currentValue))
            {
                _lastTriggerValue = null;
                return;
            }

            // 2. Debounce: If exactly the same string as last time, ignore it
            if (currentValue == _lastTriggerValue)
                return;

            _lastTriggerValue = currentValue; // Mark as processed

            // --- THE FIX IS HERE ---
            // You are sending "CMD_DRAW|{GUID}", so strict "==" will FAIL.
            // You MUST use .StartsWith to detect the command portion.
            if (currentValue.StartsWith("CMD_DRAW"))
            {
                // Correct Command -> Trigger Draw Mode
                Application.Current.Dispatcher.Invoke(() => Framemanager.StartDrawMode());
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Remote Command Received: Draw Mode ({currentValue})");
            }
            else
            {
                // Timestamp or Unknown -> Trigger Wake Up (Blockage)
                Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (Framemanager._areFramesAutoHidden)
                    {
                        Framemanager.WakeUpFrames();

                        // --- BUG FIX: The Animation Race Condition ---
                        // We must wait for the WakeUp fade-in to finish completely before 
                        // the sweep samples the window opacity. Otherwise, it captures Opacity=0 
                        // and permanently hides them after the sweep completes.
                        await System.Threading.Tasks.Task.Delay(600);
                    }

                    ActivateLighthouseSweep();
                });

                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.General, $"Remote Trigger Received: Wake Up ({currentValue})");
            }

            // 3. Cleanup registry immediately
            RegistryHelper.DeleteTrigger();
        }

        #endregion

        #region Private Methods - Dance Party (Ctrl+Alt+D)

        private static void ActivateDanceParty()
        {
            if (_isDanceActive) return;

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore: Dance Party activated!");
            _isDanceActive = true;

            try
            {
                // Play MIDI music
                PlayHappyTune2();

				// Get all frame icons
				var frameWindows = Application.Current.Windows.OfType<NonActivatingWindow>();
                var allIcons = new List<StackPanel>();

                foreach (var window in frameWindows)
                {
                    var wrapPanel = FrameUtilities.FindWrapPanel(window);
                    if (wrapPanel != null)
                    {
                        allIcons.AddRange(wrapPanel.Children.OfType<StackPanel>());
                    }
                }

                // Create bounce animation
                foreach (var icon in allIcons)
                {
                    var bounceAnimation = new DoubleAnimationUsingKeyFrames();
                    var easing = new BounceEase { EasingMode = EasingMode.EaseOut };

                    bounceAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0))));
                    bounceAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(-20, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.3))) { EasingFunction = easing });
                    bounceAnimation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(0.6))) { EasingFunction = easing });

                    bounceAnimation.RepeatBehavior = new RepeatBehavior(TimeSpan.FromSeconds(10));

                    if (icon.RenderTransform == null || icon.RenderTransform == Transform.Identity)
                        icon.RenderTransform = new TranslateTransform();

                    var transform = icon.RenderTransform as TranslateTransform ?? new TranslateTransform();
                    icon.RenderTransform = transform;

                    transform.BeginAnimation(TranslateTransform.YProperty, bounceAnimation);
                }

                // Reset flag
                var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                resetTimer.Tick += (s, e) =>
                {
                    _isDanceActive = false;
                    resetTimer.Stop();
                };
                resetTimer.Start();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error in Dance Party: {ex.Message}");
                _isDanceActive = false;
            }
        }

        private static void PlayHappyTune2()
        {
            if (SettingsManager.EnableSounds == false) return;

            try
            {
                var midiOut = new NAudio.Midi.MidiOut(0);
                midiOut.Send(NAudio.Midi.MidiMessage.ChangePatch(12, 1).RawData);

                var thread = new Thread(() =>
                {
                    int[][] chords = { new[] { 60, 64, 67 }, new[] { 67, 71, 74 }, new[] { 69, 72, 76 }, new[] { 65, 69, 72 } };

                    for (int i = 0; i < 14; i++)
                    {
                        foreach (var note in chords[i % chords.Length])
                            midiOut.Send(NAudio.Midi.MidiMessage.StartNote(note, 90, 1).RawData);

                        Thread.Sleep(300);

                        // Staccato rhythm
                        for (int j = 0; j < 4; j++)
                        {
                            int rootNote = chords[i % chords.Length][0];
                            midiOut.Send(NAudio.Midi.MidiMessage.StopNote(rootNote, 0, 1).RawData);
                            midiOut.Send(NAudio.Midi.MidiMessage.StartNote(rootNote + (j % 2 == 0 ? 0 : 7), 110, 1).RawData);
                            Thread.Sleep(100);
                        }
                        foreach (var note in chords[i % chords.Length])
                            midiOut.Send(NAudio.Midi.MidiMessage.StopNote(note, 0, 1).RawData);
                    }
                    midiOut.Dispose();
                });

                thread.IsBackground = true;
                thread.Start();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI, $"InterCore: Audio error: {ex.Message}");
            }
        }

        #endregion

        #region Private Methods - Gravity Drop (Ctrl+Shift+G)

        private static void ActivateGravityDrop()
        {
            if (_isGravityActive) return;

            LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI, "InterCore: Gravity Drop activated!");
            _isGravityActive = true;

            try
            {
                var frameWindows = Application.Current.Windows.OfType<NonActivatingWindow>();
                var allIcons = new List<StackPanel>();
                _originalIconPositions.Clear();

                foreach (var window in frameWindows)
                {
                    var wrapPanel = FrameUtilities.FindWrapPanel(window);
                    if (wrapPanel != null)
                        allIcons.AddRange(wrapPanel.Children.OfType<StackPanel>());
                }

                foreach (var icon in allIcons)
                {
                    var random = new Random();
                    var fallDistance = 500 + random.Next(100);
                    var fallDuration = 1.5 + random.NextDouble() * 0.5;

                    var fallAnimation = new DoubleAnimation
                    {
                        From = 0,
                        To = fallDistance,
                        Duration = TimeSpan.FromSeconds(fallDuration),
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    };

                    var bounceAnimation = new DoubleAnimation
                    {
                        From = fallDistance,
                        To = 0,
                        Duration = TimeSpan.FromSeconds(0.8),
                        EasingFunction = new BounceEase { EasingMode = EasingMode.EaseOut, Bounces = 3 },
                        BeginTime = TimeSpan.FromSeconds(fallDuration)
                    };

                    if (icon.RenderTransform == null || icon.RenderTransform == Transform.Identity)
                        icon.RenderTransform = new TranslateTransform();

                    var transform = icon.RenderTransform as TranslateTransform ?? new TranslateTransform();
                    icon.RenderTransform = transform;

                    transform.BeginAnimation(TranslateTransform.YProperty, fallAnimation);

                    var bounceTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(fallDuration) };
                    bounceTimer.Tick += (s, e) =>
                    {
                        transform.BeginAnimation(TranslateTransform.YProperty, bounceAnimation);
                        bounceTimer.Stop();
                    };
                    bounceTimer.Start();
                }

                var resetTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
                resetTimer.Tick += (s, e) =>
                {
                    _isGravityActive = false;
                    _originalIconPositions.Clear();
                    resetTimer.Stop();
                };
                resetTimer.Start();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"InterCore: Error in Gravity Drop: {ex.Message}");
                _isGravityActive = false;
            }
        }

        #endregion

        #region Private Methods - Lighthouse Sweep (Single Instance Effect)

        private static bool _isLighthouseSweepActive = false;

        public static void ActivateLighthouseSweep()
        {
            if (_isLighthouseSweepActive)
            {
                LogManager.Log(LogManager.LogLevel.Debug, LogManager.LogCategory.UI,
                    "InterCore: Lighthouse sweep already active, skipping");
                return;
            }

            _isLighthouseSweepActive = true;

            try
            {
                LogManager.Log(LogManager.LogLevel.Info, LogManager.LogCategory.UI,
                    "InterCore: Lighthouse Sweep effect activated - another instance detected!");

				// Get all visible frame windows
				var allFrames = Application.Current.Windows.OfType<NonActivatingWindow>()
                    .Where(w => w.Visibility == Visibility.Visible)
                    .OrderBy(w => w.Left)
                    .ToList();

                if (allFrames.Count == 0)
                {
                    LogManager.Log(LogManager.LogLevel.Warn, LogManager.LogCategory.UI,
                        "InterCore: No visible frames found for Lighthouse Sweep");
                    _isLighthouseSweepActive = false;
                    return;
                }

                // Store original opacities
                var originalOpacities = new Dictionary<NonActivatingWindow, double>();
                foreach (var frame in allFrames)
                {
                    originalOpacities[frame] = frame.Opacity;

					// If frame has high tint (low opacity), fade it to 0.4
					if (frame.Opacity > 0.4)
                    {
                        var fadeOut = new DoubleAnimation
                        {
                            To = 0.4,
                            Duration = TimeSpan.FromMilliseconds(400),
                            FillBehavior = FillBehavior.HoldEnd
                        };
						frame.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                    }
                }
                PlaySweepSound();

				// Create wave effect across frames
				for (int i = 0; i < allFrames.Count; i++)
                {
                    var frame = allFrames[i];
                    int delay = i * 150;

                    var delayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delay) };
                    delayTimer.Tick += (s, e) =>
                    {
                        ApplyLighthouseGlowToFrame(frame);
                        ((DispatcherTimer)s).Stop();
                    };
                    delayTimer.Start();
                }

                // Restore original opacities with fade-in
                var restoreTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                restoreTimer.Tick += (s, e) =>
                {
                    try
                    {
                        foreach (var frame in allFrames)
                        {
                            if (originalOpacities.ContainsKey(frame))
                            {
                                var fadeIn = new DoubleAnimation
                                {
                                    To = originalOpacities[frame],
                                    Duration = TimeSpan.FromMilliseconds(400),
                                    FillBehavior = FillBehavior.HoldEnd
                                };
								frame.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                            }
                        }

                        _isLighthouseSweepActive = false;
                        restoreTimer.Stop();
                    }
                    catch (Exception ex)
                    {
                        LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                            $"InterCore: Error restoring opacities: {ex.Message}");
                        _isLighthouseSweepActive = false;
                    }
                };
                restoreTimer.Start();
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"InterCore: Error in Lighthouse Sweep activation: {ex.Message}");
                _isLighthouseSweepActive = false;
            }
        }

        private static void ApplyLighthouseGlowToFrame(NonActivatingWindow frame)
        {
            try
            {
                var lighthouseGlow = new DropShadowEffect
                {
                    Color = Color.FromRgb(255, 215, 0), // Gold color
                    BlurRadius = 25,
                    ShadowDepth = 0,
                    Opacity = 0
                };

                var originalEffect = frame.Effect;
				frame.Effect = lighthouseGlow;

                var pulseAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 0.9,
                    Duration = TimeSpan.FromMilliseconds(200),
                    AutoReverse = true,
                    RepeatBehavior = new RepeatBehavior(3)
                };

                pulseAnimation.Completed += (s, e) =>
                {
                    try
                    {
						frame.Effect = originalEffect;
                    }
                    catch { }
                };

                lighthouseGlow.BeginAnimation(DropShadowEffect.OpacityProperty, pulseAnimation);
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI,
                    $"InterCore: Error applying lighthouse glow to frame '{frame?.Title}': {ex.Message}");
            }
        }

        #endregion

        private static void PlaySweepSound()
        {
            if (SettingsManager.EnableSounds == false) return;
            try
            {
                using (Stream soundStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Desktop_Frames.Resources.sweep-sound-effect-240243.wav"))
                {
                    if (soundStream != null)
                    {
                        using (SoundPlayer player = new SoundPlayer(soundStream)) { player.Play(); }
                    }
                }
            }
            catch (Exception ex)
            {
                LogManager.Log(LogManager.LogLevel.Error, LogManager.LogCategory.UI, $"Error playing sound: {ex.Message}");
            }
        }
    }
}