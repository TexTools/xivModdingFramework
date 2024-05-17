-- Meta table for storing framework values,
-- Such as Cache version.
CREATE TABLE "meta" (
	"key" TEXT NOT NULL UNIQUE,
	"value" TEXT,
	PRIMARY KEY("key")
);

-- All Equipment, Accessory, and Human type entries.
CREATE TABLE "characters" (
	"name"	TEXT NOT NULL,
	"primary_id"	INTEGER NOT NULL,
	"slot"		TEXT,
	"slot_full"	TEXT NOT NULL,
	"root"		TEXT
);

-- All Equipment, Accessory, and Human type entries.
CREATE TABLE "items" (
	"exd_id"	INTEGER NOT NULL,
	"name"	TEXT NOT NULL,
	"primary_id"	INTEGER NOT NULL,
	"secondary_id"	INTEGER NOT NULL,
	"is_weapon"	INTEGER NOT NULL,
	"slot"		TEXT,
	"slot_full"	TEXT NOT NULL,
	"imc_variant"	INTEGER NOT NULL,
	"icon_id"	INTEGER NOT NULL,
	"root"		TEXT,
	PRIMARY KEY("name", "exd_id")
);

-- All Dat 06000 stuff.
CREATE TABLE "ui" (
	"name" TEXT NOT NULL,
	"category" TEXT NOT NULL,
	"subcategory" TEXT,
	"mapzonecategory" TEXT,
	"path" TEXT,
	"icon_id" INTEGER NOT NULL,
	"root"		TEXT,
	
	PRIMARY KEY("name", "path", "icon_id")
);

-- All /bgcommon/ stuff.
CREATE TABLE "furniture" (
	"name" TEXT NOT NULL,
	"category" TEXT NOT NULL,
	"subcategory" TEXT,
	"primary_id"	INTEGER NOT NULL,
	"secondary_id"	INTEGER,
	"icon_id"	INTEGER NOT NULL,
	"root"		TEXT,
	
	PRIMARY KEY("category", "name", "primary_id")
);


-- Includes both known monster and demihuman types.
CREATE TABLE "monsters" (
	"name" TEXT NOT NULL,
	"category" TEXT NOT NULL,
	"primary_id"	INTEGER NOT NULL,
	"secondary_id"	INTEGER NOT NULL,
	"imc_variant"	INTEGER NOT NULL,
	"model_type" TEXT NOT NULL,
	"root"		TEXT,
	
	PRIMARY KEY("category", "name", "primary_id", "secondary_id", "imc_variant")
);

-- Human-readable names for roots.  Cached for snappy access.
CREATE TABLE "nice_root_names" (
	"root" TEXT NOT NULL,
	"nice_name" TEXT NOT NULL,

	PRIMARY KEY("root")
);

-- File children.  Only guaranteed populated for mod files.
CREATE TABLE "dependencies_children" (
	"parent" TEXT NOT NULL,
	"child" TEXT,

	PRIMARY KEY("parent", "child")
);

-- File parents.  Only guaranteed populated for mod files when the cache queue length is 0.
CREATE TABLE "dependencies_parents" (
	"child" TEXT NOT NULL,
	"parent" TEXT,

	PRIMARY KEY("child", "parent")
);

-- Cache worker queues
CREATE TABLE "dependencies_children_queue" (
	"position" INTEGER PRIMARY KEY AUTOINCREMENT,
	"file" TEXT UNIQUE NOT NULL
);

CREATE TABLE "dependencies_parents_queue" (
	"position" INTEGER PRIMARY KEY AUTOINCREMENT,
	"file" TEXT UNIQUE NOT NULL
);