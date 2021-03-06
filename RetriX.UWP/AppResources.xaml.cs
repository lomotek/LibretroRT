﻿using NavigationMenuUWP;
using RetriX.UWP.Pages;
using Windows.ApplicationModel.Resources;

namespace RetriX.UWP
{
    partial class AppResources
    {
        public AppResources()
        {
            InitializeComponent();

            var resLoader = ResourceLoader.GetForViewIndependentUse();

            this.Add("TopNavItems", new NavMenuItem[]
            {
                new NavMenuItem {Symbol = "\ue7fc", Label = resLoader.GetString("GameSystemsNavItem\\Text"), DestPage = typeof(SystemSelectionPage) },
            });

            this.Add("BottomNavItems", new NavMenuItem[]
            {
                new NavMenuItem {Symbol = "\ue115", Label = resLoader.GetString("SettingsNavItem\\Text"), DestPage = typeof(SettingsPage) },
                new NavMenuItem {Symbol = "\ue946", Label = resLoader.GetString("AboutNavItem\\Text"), DestPage = typeof(AboutPage) }
            });

        }
    }
}
