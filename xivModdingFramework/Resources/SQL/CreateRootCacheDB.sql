CREATE TABLE "roots" (
	"primary_type" TEXT NOT NULL,
	"primary_id" INT NOT NULL,
	"secondary_type" TEXT,
	"secondary_id" INT,
	"slot" TEXT,
	"root_path" TEXT NOT NULL,

	PRIMARY KEY("root_path")
);