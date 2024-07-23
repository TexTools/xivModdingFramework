-- Create hard table clones
CREATE TABLE view_shader_constant_defaults2 AS
  SELECT *
  FROM view_shader_constant_defaults;
  
CREATE TABLE view_shader_keys_reference2 AS
  SELECT *
  FROM view_shader_keys_reference;
  
CREATE TABLE view_shader_key_defaults2 AS
  SELECT *
  FROM view_shader_key_defaults;


-- Drop views and data tables
DROP VIEW view_shader_constant_defaults;
DROP VIEW view_shader_keys_reference;
DROP VIEW view_shader_key_defaults;
DROP VIEW view_shader_constant_counts;
DROP VIEW view_shader_key_counts;
DROP TABLE materials;
DROP TABLE textures;
DROP TABLE shader_constants;
DROP TABLE shader_keys;  
  
-- Rename
ALTER TABLE view_shader_constant_defaults2 RENAME TO view_shader_constant_defaults;
ALTER TABLE view_shader_keys_reference2 RENAME TO view_shader_keys_reference;
ALTER TABLE view_shader_key_defaults2 RENAME TO view_shader_key_defaults;
  
