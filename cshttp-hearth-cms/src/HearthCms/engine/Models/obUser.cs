using System;

namespace System.engine
{
    public class obUser
    {
        int id = 0;
        string username = "";
        string email = "";
        string password_hash = "";
        string display_name = "";
        string role = "admin";
        DateTime date_created = DateTime.MinValue;

        public int Id { get { return id; } set { id = value; } }
        public string Username { get { return username; } set { username = value; } }
        public string Email { get { return email; } set { email = value; } }
        public string PasswordHash { get { return password_hash; } set { password_hash = value; } }
        public string DisplayName { get { return display_name; } set { display_name = value; } }
        public string Role { get { return role; } set { role = value; } }
        public DateTime DateCreated { get { return date_created; } set { date_created = value; } }
    }
}
