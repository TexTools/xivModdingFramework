-- Meta table for storing any meta level information not already obviously handled by the table structure.
-- Ex. Author, path to original file, etc.
CREATE TABLE "meta" (
	"key" TEXT NOT NULL UNIQUE,
	"value" TEXT,
	PRIMARY KEY("key")
);

CREATE TABLE materials (
	-- Simple combined identifier of data_fule-file_offset
	"db_key" TEXT NOT NULL UNIQUE, 
	
	"data_file" INTEGER NOT NULL,
	"file_offset" INTEGER NOT NULL,
	"file_hash" INTEGER,
	"folder_hash" INTEGER,
	"full_hash" INTEGER,
	"file_path" TEXT,
	"shader_pack" TEXT,
	
	PRIMARY KEY("db_key")
);


CREATE TABLE shader_keys (
	"db_key" TEXT NOT NULL, 
	"key_id" INTEGER NOT NULL,
	"value" INTEGER NOT NULL,
	"name" TEXT
);

CREATE TABLE shader_constants (
	"db_key" TEXT NOT NULL, 
	"constant_id" INTEGER NOT NULL,
	"length" INTEGER NOT NULL,
	"value0" REAL,
	"value1" REAL,
	"value2" REAL,
	"value3" REAL,
	"name" TEXT
);

create view view_shader_keys_reference as
select distinct shader_pack, key_id, value, name from materials m left 
join shader_keys sk on sk.db_key = m.db_key
where sk.db_key is not null
order by shader_pack asc, key_id asc, value asc;

create view view_shader_key_counts as
select row_number() over (order by shader_pack asc, key_id asc, ct desc) as id, * from (select shader_pack, key_id, name, value, count(*) as ct from materials m left 
join shader_keys sc on sc.db_key = m.db_key
where sc.db_key is not null
group by shader_pack, key_id, sc.value
order by shader_pack asc, key_id asc, ct desc);

create view view_shader_key_defaults as 
select * from 
view_shader_key_counts a
inner join (
	select id, max(ct)
	from view_shader_key_counts 
	group by shader_pack, key_id
) b on a.id = b.id;

create view view_shader_constant_counts as
select row_number() over (order by shader_pack asc, constant_id asc, ct desc) as id, * from (select shader_pack, constant_id, name, sc.length, sc.value0, sc.value1, sc.value2, sc.value3, count(*) as ct from materials m left 
join shader_constants sc on sc.db_key = m.db_key
where sc.db_key is not null
group by shader_pack, constant_id, sc.value0, sc.value1, sc.value2, sc.value3
order by shader_pack asc, constant_id asc, ct desc);

create view view_shader_constant_defaults as 
select * from 
view_shader_constant_counts a
inner join (
	select id, max(ct)
	from view_shader_constant_counts 
	group by shader_pack, constant_id
) b on a.id = b.id;