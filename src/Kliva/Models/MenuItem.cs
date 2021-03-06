﻿namespace Kliva.Models
{
    public class MenuItem : BaseClass
    {
        private string _icon;
        public string Icon
        {
            get { return _icon; }
            set { Set(() => Icon, ref _icon, value); }
        }

        private string _title;
        public string Title
        {
            get { return _title; }
            set { Set(() => Title, ref _title, value); }
        }

        private MenuItemType _menuItemType;
        public MenuItemType MenuItemType
        {
            get { return _menuItemType; }
            set { Set(() => MenuItemType, ref _menuItemType, value); }
        }

        private MenuItemFontType _menuItemFontType;
        public MenuItemFontType MenuItemFontType
        {
            get { return _menuItemFontType; }
            set { Set(() => MenuItemFontType, ref _menuItemFontType, value); }
        }
    }
}
