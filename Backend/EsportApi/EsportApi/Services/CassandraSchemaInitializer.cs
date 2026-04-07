namespace EsportApi.Services
{
    public sealed class CassandraSchemaInitializer
    {
        private readonly Cassandra.ISession _cassandra;

        public CassandraSchemaInitializer(Cassandra.ISession cassandra)
        {
            _cassandra = cassandra;
        }

        public Task InitializeAsync()
        {
            _cassandra.Execute(
                "CREATE KEYSPACE IF NOT EXISTS esports WITH replication = {'class': 'SimpleStrategy', 'replication_factor': 1}");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.moves_by_match (
                    match_id text,
                    moved_at timestamp,
                    move_id uuid,
                    player_id text,
                    position int,
                    symbol text,
                    duration_ms bigint,
                    PRIMARY KEY ((match_id), moved_at, move_id)
                ) WITH CLUSTERING ORDER BY (moved_at ASC, move_id ASC)");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.matches_history_by_user (
                    user_id text,
                    played_at timestamp,
                    match_id text,
                    opponent_id text,
                    opponent_username text,
                    result text,
                    symbol text,
                    is_tournament boolean,
                    tournament_name text,
                    PRIMARY KEY ((user_id), played_at, match_id)
                ) WITH CLUSTERING ORDER BY (played_at DESC, match_id ASC)");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.leaderboard_snapshots_by_date (
                    snapshot_date date,
                    score int,
                    player_id text,
                    PRIMARY KEY ((snapshot_date), score, player_id)
                ) WITH CLUSTERING ORDER BY (score DESC, player_id ASC)");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.player_progress_history_by_user (
                    user_id text,
                    recorded_at timestamp,
                    entry_id uuid,
                    elo int,
                    coins int,
                    change_reason text,
                    PRIMARY KEY ((user_id), recorded_at, entry_id)
                ) WITH CLUSTERING ORDER BY (recorded_at DESC, entry_id DESC)");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.login_history_by_user (
                    user_id text,
                    logged_at timestamp,
                    entry_id uuid,
                    ip_address text,
                    device text,
                    PRIMARY KEY ((user_id), logged_at, entry_id)
                ) WITH CLUSTERING ORDER BY (logged_at DESC, entry_id DESC)");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.users_by_email (
                    email text PRIMARY KEY,
                    user_id text,
                    username text,
                    password_hash text,
                    created_at timestamp
                )");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.purchase_logs_by_month (
                    year_month text,
                    purchased_at timestamp,
                    purchase_id uuid,
                    user_id text,
                    item_id text,
                    item_name text,
                    price int,
                    PRIMARY KEY ((year_month), purchased_at, purchase_id)
                ) WITH CLUSTERING ORDER BY (purchased_at DESC, purchase_id DESC)");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.inventory_by_user (
                    user_id text,
                    purchased_at timestamp,
                    item_id text,
                    item_name text,
                    PRIMARY KEY ((user_id), purchased_at, item_id)
                ) WITH CLUSTERING ORDER BY (purchased_at DESC, item_id ASC)");

            _cassandra.Execute(@"
                CREATE TABLE IF NOT EXISTS esports.inventory_items_by_user (
                    user_id text,
                    item_id text,
                    item_name text,
                    purchased_at timestamp,
                    PRIMARY KEY ((user_id), item_id)
                )");

            TryAddColumn("ALTER TABLE esports.inventory_by_user ADD purchase_price int");
            TryAddColumn("ALTER TABLE esports.inventory_items_by_user ADD purchase_price int");

            return Task.CompletedTask;
        }

        private void TryAddColumn(string query)
        {
            try
            {
                _cassandra.Execute(query);
            }
            catch
            {
                // Kolona je vec dodata ili lokalna instanca trenutno ne zahteva izmenu.
            }
        }
    }
}
