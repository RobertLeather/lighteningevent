// expireAfterSeconds 0 makes the date in `expiry` the removal instant.
var database = db.getSiblingDB("Lightening");
var names = database.getCollectionNames();
if (names.indexOf("LighteningEvents") === -1) {
  database.createCollection("LighteningEvents");
}
database.LighteningEvents.createIndex(
  { expiry: 1 },
  { name: "expiry_ttl", expireAfterSeconds: 0 }
);
