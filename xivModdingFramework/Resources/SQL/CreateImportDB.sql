-- Meta table for storing any meta level information not already obviously handled by the table structure.
-- Ex. Author, path to original file, etc.
CREATE TABLE "meta" (
	"key" TEXT NOT NULL UNIQUE,
	"value" TEXT,
	PRIMARY KEY("key")
);

-- Warnings that should be proc'd by TexTools.
CREATE TABLE "warnings" (
	"text" TEXT NOT NULL
);

-- The Triangle Indices
CREATE TABLE "indices" (
	"mesh"	INTEGER NOT NULL,
	"part"   INTEGER NOT NULL,
	"index_id"	INTEGER NOT NULL,
	"vertex_id"		TEXT NOT NULL,
	

	PRIMARY KEY("mesh","part","index_id")
);


-- Vertex Data
CREATE TABLE "vertices" (
	"mesh"	INTEGER NOT NULL,
	"part"   INTEGER NOT NULL,
	"vertex_id"	INTEGER NOT NULL,

	-- Position
	"position_x"	REAL NOT NULL,
	"position_y"	REAL NOT NULL,
	"position_z"	REAL NOT NULL,

	-- Normal
	"normal_x"	REAL NOT NULL,
	"normal_y"  REAL NOT NULL,
	"normal_z"	REAL NOT NULL,
	
	-- Vertex Color
	"color_r"	REAL NOT NULL,
	"color_g"	REAL NOT NULL,
	"color_b"	REAL NOT NULL,
	"color_a"	REAL NOT NULL,

	-- UV Coordinates
	"uv_1_u"	REAL NOT NULL,
	"uv_1_v"	REAL NOT NULL,
	"uv_2_u"	REAL NOT NULL,
	"uv_2_v"	REAL NOT NULL,

	-- Bone Weights
	"bone_1_id"			INTEGER,
	"bone_1_weight"		REAL,
	"bone_2_id"			INTEGER,
	"bone_2_weight"		REAL,
	"bone_3_id"			INTEGER,
	"bone_3_weight"		REAL,
	"bone_4_id"			INTEGER,
	"bone_4_weight"		REAL,

	PRIMARY KEY("mesh","part","vertex_id")
);

-- Parts
CREATE TABLE "parts" (
	"mesh"	INTEGER NOT NULL,
	"part"   INTEGER NOT NULL,
	"name"   TEXT,

	Primary KEY("mesh", "part")
);

-- Bones
CREATE TABLE "bones" (
	"bone_id"	INTEGER NOT NULL,
	"name"  TEXT NOT NULL,
	
	-- Format for storing bone matrices tbd.

	Primary KEY("bone_id")
);

-- Materials
CREATE TABLE "materials" (
	"mesh"			INTEGER NOT NULL,
	"part"			INTEGER NOT NULL,
	"usage"			TEXT NOT NULL,
	"path"			TEXT NOT NULL,

	Primary KEY("mesh","part","usage")
);