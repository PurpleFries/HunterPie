﻿using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using HunterPie.Core;
using HunterPie.Core.Monsters;
using HunterPie.Core.Events;
using Timer = System.Threading.Timer;
using HunterPie.Logger;
using HunterPie.Core.Settings;

namespace HunterPie.GUI.Widgets.Monster_Widget.Parts
{
    /// <summary>
    /// Interaction logic for MonsterPart.xaml
    /// </summary>
    public partial class MonsterPart : UserControl, IComparable<MonsterPart>
    {
        public Part context;
        private Timer visibilityTimer;
        public DateTime LastAppeared = DateTime.UtcNow;

        public MonsterPart() => InitializeComponent();

        private static Monsterscomponent ComponentSettings =>
            ConfigManager.Settings.Overlay.MonstersComponent;

        public string PartName
        {
            get { return (string)GetValue(PartNameProperty); }
            set { SetValue(PartNameProperty, value); }
        }
        public static readonly DependencyProperty PartNameProperty =
            DependencyProperty.Register("PartName", typeof(string), typeof(MonsterPart));

        public string PartBrokenCounter
        {
            get { return (string)GetValue(PartBrokenCounterProperty); }
            set { SetValue(PartBrokenCounterProperty, value); }
        }
        public static readonly DependencyProperty PartBrokenCounterProperty =
            DependencyProperty.Register("PartBrokenCounter", typeof(string), typeof(MonsterPart));

        public string PartHealthText
        {
            get { return (string)GetValue(PartHealthTextProperty); }
            set { SetValue(PartHealthTextProperty, value); }
        }
        public static readonly DependencyProperty PartHealthTextProperty =
            DependencyProperty.Register("PartHealthText", typeof(string), typeof(MonsterPart));

        private int specialThreshold = int.MaxValue;

        public void SetContext(Part ctx, double MaxHealthBarSize)
        {
            context = ctx;
            SetPartInformation(MaxHealthBarSize);
            HookEvents();
            StartVisibilityTimer();
        }

        private void HookEvents()
        {
            context.OnHealthChange += OnHealthChange;
            context.OnBrokenCounterChange += OnBrokenCounterChange;
            context.OnTenderizeStateChange += OnTenderizeStateChange;
        }

        public void UnhookEvents()
        {
            context.OnHealthChange -= OnHealthChange;
            context.OnBrokenCounterChange -= OnBrokenCounterChange;
            context.OnTenderizeStateChange -= OnTenderizeStateChange;
            visibilityTimer?.Dispose();
            context = null;
        }

        private void Dispatch(Action f) => Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Render, f);

        #region Visibility timer
        private void StartVisibilityTimer()
        {
            LastAppeared = DateTime.UtcNow;
            if (!ComponentSettings.HidePartsAfterSeconds)
            {
                ApplySettings();
                return;
            }
            if (visibilityTimer == null)
            {
                visibilityTimer = new Timer(_ => HideInactiveBar(), null, 10, 0);
            }
            else
            {
                visibilityTimer.Change(ComponentSettings.SecondsToHideParts * 1000, 0);
            }
        }

        private void HideInactiveBar() => Dispatch(() =>
        {
            Visibility = Visibility.Collapsed;
        });

        #endregion

        #region Settings
        public void ApplySettings()
        {
            Visibility visibility = GetVisibility();
            if (ComponentSettings.HidePartsAfterSeconds) visibility = Visibility.Collapsed;
            Dispatch(() => { Visibility = visibility; UpdateHealthText(); });
        }

        private Visibility GetVisibility()
        {
            // Hide invalid parts, like the Vaal Hazaak unknown ones
            if (float.IsNaN(context.TotalHealth) || context.TotalHealth <= 0)
                return Visibility.Collapsed;

            // Hide parts that cannot be broken
            bool canBeBroken = context.BreakThresholds.Length > 0;
            bool isTenderized = context.TenderizeDuration > 0 && context.TenderizeDuration < context.TenderizeMaxDuration;
            if (ComponentSettings.EnableOnlyPartsThatCanBeBroken)
            {
                // Shows tenderized parts
                if (isTenderized)
                {
                    return Visibility.Visible;
                }

                if (canBeBroken)
                {
                    bool isBroken = context.BreakThresholds.LastOrDefault().Threshold <= context.BrokenCounter;
                    if (ComponentSettings.HidePartsThatHaveAlreadyBeenBroken && isBroken)
                    {
                        return Visibility.Collapsed;
                    }
                } else
                {
                    return Visibility.Collapsed;
                }
            }

            if (canBeBroken && ComponentSettings.HidePartsThatHaveAlreadyBeenBroken)
            {
                bool isBroken = context.HasBreakConditions ? specialThreshold <= context.BrokenCounter
                    : context.BreakThresholds.LastOrDefault().Threshold <= context.BrokenCounter;
                return isBroken && !isTenderized ? Visibility.Collapsed : Visibility.Visible;
            }

            if (context.IsRemovable)
            {
                return ComponentSettings.EnableRemovableParts ? Visibility.Visible : Visibility.Collapsed;
            }

            if (ComponentSettings.EnableMonsterParts && ComponentSettings.EnabledPartGroups.Contains(context.Group))
            {
                return Visibility.Visible;
            }
            
            return Visibility.Collapsed;
        }

        #endregion

        #region Events

        private void SetPartInformation(double newSize)
        {
            PartName = $"{context.Name}";
            if (context.HasBreakConditions)
            {
                HandleSpecialBrokenCounter();
            } else
            {
                UpdatePartBrokenCounter();
            }
            UpdateHealthSize(newSize);
            UpdateHealthText();
            ApplySettings();
        }

        private void OnBrokenCounterChange(object source, MonsterPartEventArgs args)
        {
            Visibility visibility = GetVisibility();
            Dispatch(() =>
            {
                if (args.HasBreakConditions)
                {
                    HandleSpecialBrokenCounter();
                } else
                {
                    UpdatePartBrokenCounter();
                }
                Visibility = visibility;
                StartVisibilityTimer();
            });
        }

        private void OnTenderizeStateChange(object source, MonsterPartEventArgs args)
        {
            Visibility visibility = args.Duration > 0 ? Visibility.Visible : Visibility.Collapsed;
            Dispatch(() =>
            {
                TenderizeBar.Value = args.MaxDuration - args.Duration;
                TenderizeBar.MaxValue = args.MaxDuration;
                TenderizeBar.Visibility = visibility;
                Visibility = GetVisibility();
                UpdateHealthText();
                StartVisibilityTimer();
            });
        }

        private void OnHealthChange(object source, MonsterPartEventArgs args)
        {
            Visibility visibility = GetVisibility();
            Dispatch(() =>
            {
                if (args.HasBreakConditions)
                    HandleSpecialBrokenCounter();

                UpdateHealthText();
                Visibility = visibility;
                StartVisibilityTimer();
            });
        }

        public void UpdateHealthBarSize(double newSize)
        {
            if (context == null) return;
            UpdateHealthSize(newSize);
            UpdateHealthText();
            ApplySettings();
        }

        private void UpdatePartBrokenCounter()
        {
            string suffix = "";
            for (int i = context.BreakThresholds.Length - 1; i >= 0; i--)
            {
                int threshold = context.BreakThresholds[i].Threshold;
                if (context.BrokenCounter < threshold || i == context.BreakThresholds.Length - 1)
                {
                    suffix = $"/{threshold}";
                    if (i < context.BreakThresholds.Length - 1)
                    {
                        suffix += "+";
                    }
                }
            }
            PartBrokenCounter = $"{context.BrokenCounter}{suffix}";
        }

        private void HandleSpecialBrokenCounter()
        {
            int healthRatio = (int)((context.Owner.Health / context.Owner.MaxHealth) * 100);
            ThresholdInfo threshold = context.BreakThresholds.First();

            if (healthRatio < threshold.MinHealth && context.BrokenCounter >= threshold.MinFlinch)
            {
                specialThreshold = Math.Min(context.BrokenCounter + 1, specialThreshold);
                PartBrokenCounter = $"{context.BrokenCounter}/{specialThreshold}";
            } else
            {
                PartBrokenCounter = $"{context.BrokenCounter}";
            }
        }

        public void UpdateHealthSize(double newSize)
        {
            PartHealth.MaxSize = newSize - 37;
            TenderizeBar.MaxSize = newSize - 37;
            PartHealth.MaxValue = context.TotalHealth;
            PartHealth.Value = context.Health;
        }

        private void UpdateHealthText()
        {
            if (context is null)
            {
                return;
            }
            PartHealth.MaxValue = context.TotalHealth;
            PartHealth.Value = context.Health;
            double percentage = PartHealth.Value / Math.Max(1, PartHealth.MaxValue);
            TimeSpan TVal = TimeSpan.FromSeconds(context.TenderizeMaxDuration - context.TenderizeDuration);
            string TenderizeTimer = (TVal.Ticks > 0 ? $"{TVal:mm\\:ss}" :"");
            string format = ConfigManager.Settings.Overlay.MonstersComponent.PartTextFormat;
            PartHealthText = format.Replace("{Current}", $"{PartHealth.Value:0}")
                .Replace("{Max}", $"{PartHealth.MaxValue:0}")
                .Replace("{Percentage}", $"{percentage * 100:0}")
                //.Replace("{Tenderize}", $"{TimeSpan.FromSeconds(context.TenderizeMaxDuration - context.TenderizeDuration):mm\\:ss}");
                .Replace("{Tenderize}", TenderizeTimer);
        }

        public int CompareTo(MonsterPart other)
        {
            float otherTenderize = other.context.TenderizeDuration % other.context.TenderizeMaxDuration;
            float thisTenderize = context.TenderizeDuration % context.TenderizeMaxDuration;
            if (otherTenderize > thisTenderize)
            {
                return -1;
            } else
            {
                if (otherTenderize == 0 && thisTenderize > 0)
                {
                    return 1;
                } else
                {
                    return other.LastAppeared > LastAppeared ? -1 : 1;
                }
            }
        }

        #endregion
    }
}
