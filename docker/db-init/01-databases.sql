-- First start of the database container only. Accounts and the server list (lmemu_auth), the shared characters
-- (lmemu_character), and one database per world server (lmemu_world_<name>, created by that world on its first start).
-- The servers create and update their own tables (migrations at startup).
CREATE DATABASE IF NOT EXISTS lmemu_auth CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
CREATE DATABASE IF NOT EXISTS lmemu_character CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;
GRANT ALL PRIVILEGES ON lmemu_auth.* TO 'landmarkemu'@'%';
GRANT ALL PRIVILEGES ON lmemu_character.* TO 'landmarkemu'@'%';
GRANT ALL PRIVILEGES ON `lmemu\_world\_%`.* TO 'landmarkemu'@'%';
-- The shared social service's own database (created by the service on its first start; docker/social).
GRANT ALL PRIVILEGES ON lmemu_social.* TO 'landmarkemu'@'%';
-- Each world's world-channel service's own database lmemu_wc_<world> (created by the service on its first start; docker/worldchannel).
GRANT ALL PRIVILEGES ON `lmemu\_wc\_%`.* TO 'landmarkemu'@'%';
-- The shared commerce service's own database (created by the service on its first start; docker/commerce).
GRANT ALL PRIVILEGES ON lmemu_commerce.* TO 'landmarkemu'@'%';
FLUSH PRIVILEGES;
